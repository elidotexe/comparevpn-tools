using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Net;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// This class is the ISP checker's phone book. It reads the free, public-domain list of networks from
/// <see href="https://iptoasn.com">iptoasn.com</see> into memory and finds which network owns an address.
/// Everything happens on this server, so no visitor's address is ever sent anywhere else.
///
/// In plain terms, here is what happens:
///   1. The first time someone asks, it reads the whole file (plain or compressed) and sorts its blocks of addresses.
///   2. To answer a question, it finds the block that contains the address and returns the network that owns it.
///   3. When asked to check for a newer file, it reads the new file in full and only then swaps it in, so lookups
///      never wait and never see half a file.
///   If the file isn't there yet, it answers "data not available" and tries again next time. A broken new file never
///   replaces a good one: the old list keeps being used.
/// </summary>
public sealed class IpToAsnFileDataSource(string filePath) : IReloadableAsnDataSource
{
    private readonly Lock _firstLoadGate = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private Task<Dataset?>? _firstLoad;

    // The list in use. Replaced as a whole by a reload, so a lookup always sees one complete copy.
    private volatile Dataset? _current;

    /// <inheritdoc />
    public string FilePath => filePath;

    /// <inheritdoc />
    public bool IsLoaded => _current is not null;

    /// <inheritdoc />
    public DateTimeOffset? DataUpdatedAt => _current?.UpdatedAt;

    /// <summary>
    /// Step 2: Finds the network that owns <paramref name="address"/>, reading the file first if this is the first
    /// question (step 1). Throws if the file exists but can't be read.
    /// </summary>
    public async ValueTask<AsnSearchResult> FindAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        // Step 2a: Use the loaded list, or read the file now if nothing is loaded yet.
        var dataset = _current ?? await LoadFirstCopyAsync().WaitAsync(cancellationToken);
        if (dataset is null)
        {
            return AsnSearchResult.DataNotAvailable();
        }

        // Step 2b: Find the block that contains the address.
        if (FindRange(dataset.Ranges, ToKey(address)) is not { } range)
        {
            return AsnSearchResult.NotInDataset(dataset.UpdatedAt);
        }

        // Step 2c: Hand back the owner and the block, written as ordinary addresses again.
        return AsnSearchResult.Found(new AsnLookup(range.Record, FromKey(range.Start), FromKey(range.End)), dataset.UpdatedAt);
    }

    /// <summary>
    /// Step 3: Loads the file again if it has changed since it was last read, or for the first time if nothing is
    /// loaded yet. Throws if the new file can't be read; the copy already in use then stays.
    /// </summary>
    public async Task<DatasetReloadOutcome> ReloadIfChangedAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            // Step 3a: Nothing loaded yet (for example, the file appeared after start-up): do the first load instead.
            if (_current is not { } current)
            {
                var firstCopy = await LoadFirstCopyAsync().WaitAsync(cancellationToken);
                return firstCopy is null ? DatasetReloadOutcome.FileMissing : DatasetReloadOutcome.Loaded;
            }

            // Step 3b: A missing or unchanged file means keep using the current copy.
            if (!File.Exists(filePath) || LastChanged(filePath) == current.UpdatedAt)
            {
                return DatasetReloadOutcome.NoChange;
            }

            // Step 3c: Read the new file in full, away from the lookups, before swapping it in.
            if (await Task.Run(() => ReadFileAsync(filePath), cancellationToken) is not { } newCopy)
            {
                return DatasetReloadOutcome.NoChange; // The file disappeared again just before it was read.
            }

            _current = newCopy;
            return DatasetReloadOutcome.Loaded;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    // ───── Helpers for reading the file (steps 1 and 3) ─────

    /// <summary>
    /// Step 1: Starts reading the file once and shares that work with everyone who asks while it runs. Returns
    /// <c>null</c> when the file isn't there. After a missing file or a failed read, the next call tries again.
    /// </summary>
    private Task<Dataset?> LoadFirstCopyAsync()
    {
        lock (_firstLoadGate)
        {
            if (NeedsNewAttempt(_firstLoad))
            {
                _firstLoad = Task.Run(async () =>
                {
                    var dataset = await ReadFileAsync(filePath);
                    if (dataset is not null)
                    {
                        _current ??= dataset; // A reload may already have put in a newer copy; keep that one.
                    }

                    return dataset;
                });
            }

            return _firstLoad;
        }
    }

    /// <summary>True when no read is running or has succeeded, so a new attempt should start.</summary>
    private static bool NeedsNewAttempt([NotNullWhen(false)] Task<Dataset?>? attempt) =>
        attempt is null
        || attempt.IsFaulted
        || attempt.IsCanceled
        || (attempt.IsCompletedSuccessfully && attempt.Result is null);

    /// <summary>
    /// Reads and sorts the whole file. Returns <c>null</c> when the file doesn't exist, and throws when it has no
    /// usable lines (for example after a failed download), so a broken file is never used.
    /// </summary>
    internal static async Task<Dataset?> ReadFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // Note the time first: if the file changes while it is being read, the next check sees it as newer.
        var updatedAt = LastChanged(path);
        var ranges = await ReadRangesAsync(path);

        if (ranges.Count == 0)
        {
            throw new InvalidDataException($"IP-to-ASN dataset '{path}' contains no valid ranges.");
        }

        // Lookups search the blocks in order of their first address (see FindRange).
        ranges.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return new Dataset([.. ranges], updatedAt);
    }

    private static async Task<List<Range>> ReadRangesAsync(string path)
    {
        await using var stream = OpenForReading(path);
        using var reader = new StreamReader(stream);

        // Room for about 500,000 blocks, roughly the size of the real file, so the list rarely has to grow.
        var ranges = new List<Range>(capacity: 1 << 19);
        var sharedRecords = new Dictionary<(int Asn, string? Country), AsnRecord>();

        while (await reader.ReadLineAsync() is { } line)
        {
            if (TryReadLine(line, sharedRecords, out var range))
            {
                ranges.Add(range);
            }
        }

        return ranges;
    }

    /// <summary>Opens the file, unpacking it on the fly when it is compressed (<c>.gz</c>).</summary>
    private static Stream OpenForReading(string path)
    {
        var file = File.OpenRead(path);
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
    }

    /// <summary>
    /// Reads one line: first address, last address, network number, country code and network name, separated by
    /// tabs. Returns false for lines that don't fit, and for network number 0, which the file uses for "not routed".
    /// </summary>
    private static bool TryReadLine(string line, Dictionary<(int Asn, string? Country), AsnRecord> sharedRecords, out Range range)
    {
        range = default;
        var parts = line.Split('\t');
        if (parts.Length < 5
            || !IPAddress.TryParse(parts[0], out var start)
            || !IPAddress.TryParse(parts[1], out var end)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var asn)
            || asn == 0)
        {
            return false;
        }

        // One shared record per network and country keeps about 500,000 blocks cheap in memory.
        var country = parts[3] is { Length: 2 } code ? code.ToUpperInvariant() : null;
        if (!sharedRecords.TryGetValue((asn, country), out var record))
        {
            record = new AsnRecord(asn, parts[4].Trim(), country);
            sharedRecords[(asn, country)] = record;
        }

        range = new Range(ToKey(start), ToKey(end), record);
        return true;
    }

    private static DateTimeOffset LastChanged(string path) => new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

    // ───── Helpers for finding an address (step 2) ─────

    /// <summary>
    /// Finds the block containing <paramref name="key"/>, or <c>null</c>. Uses a binary search: look at the middle of
    /// the sorted list, keep the half the address must be in, and repeat; about 20 steps cover the whole file.
    /// </summary>
    private static Range? FindRange(Range[] ranges, UInt128 key)
    {
        // Find the last block that starts at or before the address.
        int low = 0, high = ranges.Length - 1, match = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (ranges[middle].Start <= key)
            {
                match = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        // The address may still fall in a gap after that block ends.
        return match >= 0 && key <= ranges[match].End ? ranges[match] : null;
    }

    /// <summary>
    /// Turns an address into one big number, so addresses can be sorted and compared. IPv4 addresses are first
    /// written in IPv6 form (<c>::ffff:a.b.c.d</c>), so both kinds share one sorted list.
    /// </summary>
    private static UInt128 ToKey(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[16];
        address.MapToIPv6().TryWriteBytes(bytes, out _);
        return new UInt128(
            BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));
    }

    /// <summary>Turns a number from <see cref="ToKey"/> back into an address, as IPv4 when it was one.</summary>
    private static IPAddress FromKey(UInt128 key)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes[..8], (ulong)(key >> 64));
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)key);
        var address = new IPAddress(bytes);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    /// <summary>One loaded copy of the file: its blocks sorted by first address, and when the file last changed.</summary>
    internal sealed record Dataset(Range[] Ranges, DateTimeOffset UpdatedAt);

    /// <summary>One block of addresses, as numbers from <see cref="ToKey"/>, and the network that owns it.</summary>
    internal readonly record struct Range(UInt128 Start, UInt128 End, AsnRecord Record);
}
