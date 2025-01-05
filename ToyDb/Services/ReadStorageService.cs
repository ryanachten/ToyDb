using Microsoft.Extensions.Logging;
using ToyDb.Caches;
using ToyDb.Extensions;
using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;

namespace ToyDb.Services;

public class ReadStorageService : IReadStorageService
{
    private readonly IKeyOffsetCache _keyOffsetCache;
    private readonly IKeyEntryCache _keyEntryCache;
    private readonly IDataStoreRepository _storeRepository;
    private readonly ILogger<ReadStorageService> _logger;

    public ReadStorageService(
        IKeyOffsetCache keyOffsetCache,
        IKeyEntryCache keyEntryCache,
        IDataStoreRepository storeRepository,
        ILogger<ReadStorageService> logger)
    {
        _keyOffsetCache = keyOffsetCache;
        _keyEntryCache = keyEntryCache;
        _storeRepository = storeRepository;
        _logger = logger;

        // FIXME: there is also a bug when restoring from file, I think it's to do with delete markers
        RestoreIndexFromStore();
    }

    public DatabaseEntry GetValue(string key)
    {
        // First attempt to entry result in cache
        var hasEntry = _keyEntryCache.TryGetValue(key, out var cachedEntry);
        if (hasEntry && cachedEntry != null) return cachedEntry;
        if (hasEntry && cachedEntry == null) return DatabaseEntry.Null(key);

        // Then attempt to locate key in cache (if absent, we probably don't have a result)
        var hasOffset = _keyOffsetCache.TryGetValue(key, out long offset);
        if (!hasOffset) return DatabaseEntry.Null(key);

        // If we have a key, locate entry from file and cache result for later
        var storedEntry = _storeRepository.GetValue(offset);
        _keyEntryCache.Set(key, storedEntry);

        return storedEntry;
    }

    public Dictionary<string, DatabaseEntry> GetValues()
    {
        var entries = _storeRepository.GetLatestEntries();
        return entries.ToDictionary((x) => x.Key, (x) => x.Value.Item1);
    }

    /// <summary>
    /// Restores database index from data store
    /// </summary>
    private void RestoreIndexFromStore()
    {
        var timer = _logger.StartTimedLog(nameof(RestoreIndexFromStore));

        var entries = _storeRepository.GetLatestEntries();

        var updatedIndex = entries.ToDictionary(x => x.Key, x => x.Value.Item2);
        _keyOffsetCache.Replace(updatedIndex);

        timer.Stop();
    }
}
