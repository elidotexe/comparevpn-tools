using System.Net;
using CompareVPN.Data;
using MaxMind.Db;

namespace CompareVPN.Data.Location;

/// <summary>
/// This class is the tools' atlas. It reads the free "IP to City Lite" file from
/// <see href="https://db-ip.com">DB-IP</see> and finds roughly where an address is: city, region, country and the
/// city's coordinates. Everything happens on this server, so no visitor's address is ever sent anywhere else.
///
/// In plain terms, here is what happens:
///   1. When asked to check the file (at start-up, then every minute), it opens the file if it is new or has changed.
///      The file is a ready-made search tree, so it is used as it is rather than read into a list first.
///   2. To answer a question, it follows the tree to the address's entry and picks out the place's details.
///   3. A newer file is opened in full before it replaces the old one, and the old one is closed only once no
///      lookup is still using it.
///   Location is an extra: until a file is loaded, or for addresses the file doesn't know, it answers "no location"
///   and the rest of the lookup goes ahead. A broken new file never replaces a good one.
/// </summary>
/// <remarks>
/// The file is licensed under Creative Commons Attribution 4.0: any page that shows its results must link to
/// https://db-ip.com (see the README).
/// </remarks>
public sealed class DbIpCityFileDataSource(string filePath) : ILocationDataSource, IReloadableDataFile, IDisposable
{
    // Only one check of the file runs at a time.
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    // Lookups share the open file; swapping in a new one waits until they are done, so a file is never closed while
    // a lookup is reading it.
    private readonly ReaderWriterLockSlim _swapLock = new();

    private OpenFile? _current;

    /// <inheritdoc />
    public string FilePath => filePath;

    /// <inheritdoc />
    public bool IsLoaded => Volatile.Read(ref _current) is not null;

    /// <inheritdoc />
    public DateTimeOffset? DataUpdatedAt => Volatile.Read(ref _current)?.UpdatedAt;

    /// <summary>
    /// Step 2: Finds roughly where <paramref name="address"/> is. Returns <c>null</c> when nothing is loaded yet, the
    /// address isn't in the file, or the file knows nothing useful about it.
    /// </summary>
    public ValueTask<IpLocation?> FindAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        _swapLock.EnterReadLock();
        try
        {
            var entry = _current?.Reader.Find<DbIpCityRecord>(address);
            return ValueTask.FromResult(Describe(entry));
        }
        finally
        {
            _swapLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Steps 1 and 3: Opens the file if it has changed since it was last opened, or for the first time. Throws if the
    /// new file can't be read; the copy already in use then stays.
    /// </summary>
    public async Task<DatasetReloadOutcome> ReloadIfChangedAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            // Step 1a: No file. Keep whatever is loaded; with nothing loaded, lookups answer without a location.
            if (!File.Exists(filePath))
            {
                return _current is null ? DatasetReloadOutcome.FileMissing : DatasetReloadOutcome.NoChange;
            }

            // Step 1b: The same file as before.
            var lastChanged = LastChanged(filePath);
            if (_current?.UpdatedAt == lastChanged)
            {
                return DatasetReloadOutcome.NoChange;
            }

            // Step 1c: Open the new file away from the lookups (this throws if it is broken), then swap it in (step 3).
            var reader = await Task.Run(() => new Reader(filePath, FileAccessMode.Memory), cancellationToken);
            SwapIn(new OpenFile(reader, lastChanged));
            return DatasetReloadOutcome.Loaded;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    /// <summary>Closes the open file. Called by the website when it shuts down.</summary>
    public void Dispose()
    {
        _current?.Reader.Dispose();
        _swapLock.Dispose();
        _reloadGate.Dispose();
    }

    // ───── Helpers for describing a place (step 2) ─────

    /// <summary>Turns an entry from the file into what visitors see, or <c>null</c> when it holds nothing useful.</summary>
    private static IpLocation? Describe(DbIpCityRecord? entry)
    {
        if (entry is null || (entry.City, entry.Region, entry.CountryCode, entry.Latitude) is (null, null, null, null))
        {
            return null;
        }

        return new IpLocation(
            NullIfBlank(entry.City), NullIfBlank(entry.Region), NullIfBlank(entry.CountryCode), entry.Latitude, entry.Longitude);
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    // ───── Helpers for opening the file (steps 1 and 3) ─────

    private static DateTimeOffset LastChanged(string path) => new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

    /// <summary>
    /// Step 3: Puts the new file in use, then closes the old one. Taking the write side of the lock waits for lookups
    /// still reading the old file, and later lookups only see the new one, so closing it afterwards is safe.
    /// </summary>
    private void SwapIn(OpenFile newFile)
    {
        OpenFile? oldFile;
        _swapLock.EnterWriteLock();
        try
        {
            oldFile = _current;
            Volatile.Write(ref _current, newFile);
        }
        finally
        {
            _swapLock.ExitWriteLock();
        }

        oldFile?.Reader.Dispose();
    }

    /// <summary>An open location file and when it was last changed on disk.</summary>
    private sealed record OpenFile(Reader Reader, DateTimeOffset UpdatedAt);
}
