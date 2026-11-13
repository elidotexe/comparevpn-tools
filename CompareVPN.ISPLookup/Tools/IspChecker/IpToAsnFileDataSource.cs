using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Net;

using CompareVPN.Tools.IspChecker;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// Reads the free, public-domain <see href="https://iptoasn.com">iptoasn.com</see> dataset
/// (<c>ip2asn-combined.tsv</c> or <c>.tsv.gz</c>) into memory and answers lookups locally,
/// so no visitor IP is ever sent to a third party.
/// </summary>
/// <remarks>
/// The file is loaded on first use; if that fails, the next lookup retries.
/// <see cref="ReloadIfChangedAsync"/> swaps in a newer file without downtime: lookups keep using
/// the current copy until the new one has fully loaded, and a broken file never replaces a good one.
/// </remarks>
public sealed class IpToAsnFileDataSource(string filePath) : IReloadableAsnDataSource
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private Task<Dataset>? _loading;
    private volatile Dataset? _current;

    /// <inheritdoc />
    public bool IsLoaded => _current is not null;

    /// <inheritdoc />
    public DateTimeOffset? DataUpdatedAt => _current?.UpdatedAt;

    /// <inheritdoc />
    public async ValueTask<AsnLookup?> FindAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var dataset = _current ?? await GetInitialDatasetAsync().WaitAsync(cancellationToken);
        var ranges = dataset.Ranges;
        var key = ToKey(address);

        // Binary search for the last range starting at or before the address.
        int low = 0, high = ranges.Length - 1, match = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            if (ranges[mid].Start <= key)
            {
                match = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (match < 0 || key > ranges[match].End)
        {
            return null;
        }

        var range = ranges[match];
        return new AsnLookup(range.Record, FromKey(range.Start), FromKey(range.End), dataset.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task<bool> ReloadIfChangedAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            var current = _current;
            if (current is null)
            {
                // Nothing loaded yet (e.g. the file appeared after startup): do the initial load.
                await GetInitialDatasetAsync().WaitAsync(cancellationToken);
                return true;
            }

            if (!File.Exists(filePath) || GetLastWriteTime(filePath) == current.UpdatedAt)
            {
                return false; // Missing or unchanged: keep serving the current copy.
            }

            // Loads fully before swapping; on failure the exception propagates and the old copy stays.
            _current = await Task.Run(() => LoadAsync(filePath), cancellationToken);
            return true;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private Task<Dataset> GetInitialDatasetAsync()
    {
        lock (_gate)
        {
            if (_loading is null || _loading.IsFaulted || _loading.IsCanceled)
            {
                _loading = Task.Run(async () =>
                {
                    var dataset = await LoadAsync(filePath);
                    _current ??= dataset;
                    return dataset;
                });
            }

            return _loading;
        }
    }

    private static DateTimeOffset GetLastWriteTime(string path) =>
        new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

    internal static async Task<Dataset> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "IP-to-ASN dataset not found. Download it with scripts/download-data.sh.", path);
        }

        // Read the timestamp first: if the file changes while loading, the next check sees a newer one.
        var updatedAt = GetLastWriteTime(path);

        await using var file = File.OpenRead(path);
        Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;

        await using (stream)
        {
            using var reader = new StreamReader(stream);
            var ranges = new List<Range>(capacity: 1 << 19);
            var records = new Dictionary<(int Asn, string? Country), AsnRecord>();

            while (await reader.ReadLineAsync() is { } line)
            {
                if (TryParse(line, records, out var range))
                {
                    ranges.Add(range);
                }
            }

            if (ranges.Count == 0)
            {
                throw new InvalidDataException($"IP-to-ASN dataset '{path}' contains no valid ranges.");
            }

            ranges.Sort(static (a, b) => a.Start.CompareTo(b.Start));
            return new Dataset([.. ranges], updatedAt);
        }
    }

    // Line format: range_start \t range_end \t AS_number \t country_code \t AS_description
    private static bool TryParse(string line, Dictionary<(int Asn, string? Country), AsnRecord> cache, out Range range)
    {
        range = default;
        var parts = line.Split('\t');
        if (parts.Length < 5
            || !IPAddress.TryParse(parts[0], out var start)
            || !IPAddress.TryParse(parts[1], out var end)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var asn)
            || asn == 0) // ASN 0 = "Not routed"
        {
            return false;
        }

        // Share one record instance per (ASN, country) to keep ~500k ranges cheap in memory.
        var country = parts[3] is { Length: 2 } code ? code.ToUpperInvariant() : null;
        if (!cache.TryGetValue((asn, country), out var record))
        {
            record = new AsnRecord(asn, parts[4].Trim(), country);
            cache[(asn, country)] = record;
        }

        range = new Range(ToKey(start), ToKey(end), record);
        return true;
    }

    // IPv4 and IPv6 share one sorted key space: IPv4 is stored as IPv4-mapped IPv6 (::ffff:a.b.c.d).
    private static UInt128 ToKey(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[16];
        address.MapToIPv6().TryWriteBytes(bytes, out _);
        return new UInt128(
            BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));
    }

    private static IPAddress FromKey(UInt128 key)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes[..8], (ulong)(key >> 64));
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)key);
        var address = new IPAddress(bytes);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    internal sealed record Dataset(Range[] Ranges, DateTimeOffset UpdatedAt);

    internal readonly record struct Range(UInt128 Start, UInt128 End, AsnRecord Record);
}
