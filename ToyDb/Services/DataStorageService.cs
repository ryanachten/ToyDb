using ToyDb.Caches;
using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;

namespace ToyDb.Services
{
    public class DataStorageService : IDataStorageService
    {
        private readonly IKeyOffsetCache _keyOffsetCache;
        private readonly IKeyEntryCache _keyEntryCache;
        private readonly IDataStoreRepository _storeRepository;
        private readonly IWriteAheadLogRepository _walRepository;

        public DataStorageService(
            IKeyOffsetCache keyOffsetCache,
            IKeyEntryCache keyEntryCache,
            IDataStoreRepository storeRepository,
            IWriteAheadLogRepository walRepository)
        {
            _keyOffsetCache = keyOffsetCache;
            _keyEntryCache = keyEntryCache;
            _storeRepository = storeRepository;
            _walRepository = walRepository;

            RestoreIndexFromStore();
        }

        /// <summary>
        /// Compacts logs by storing only the latest entries in a new file
        /// </summary>
        public void CompactLogs()
        {
            if (!_storeRepository.HasRedundantData()) return;

            var entities = _storeRepository.GetLatestEntries().Select(x => x.Value.Item1);

            _storeRepository.CreateNewLogFile();

            var updatedIndex = _storeRepository.AppendRange(entities);
            _keyOffsetCache.Reset(updatedIndex);
        }

        public DatabaseEntry GetValue(string key)
        {
            // First attempt to entry result in cache
            var hasEntry = _keyEntryCache.TryGetValue(key, out var cachedEntry);
            if (hasEntry && cachedEntry != null) return cachedEntry;
            if (hasEntry && cachedEntry == null) return DatabaseEntry.Empty(key);

            // Then attempt to locate key in cache (if absent, we probably don't have a result)
            var hasOffset = _keyOffsetCache.TryGetValue(key, out long offset);
            if (!hasOffset) return DatabaseEntry.Empty(key);

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
        /// Write value to both write-ahead log and data store
        /// </summary>
        /// <param name="key">Key to assign value to</param>
        /// <param name="value">Value to assign to key</param>
        /// <returns>Saved database entry</returns>
        public DatabaseEntry SetValue(string key, DatabaseEntry value)
        {
            _walRepository.Append(key, value);
            var offset = _storeRepository.Append(key, value);

            _keyOffsetCache.Set(key, offset);

            return value;
        }

        /// <summary>
        /// Restores database index from data store
        /// </summary>
        private void RestoreIndexFromStore()
        {
            var entries = _storeRepository.GetLatestEntries();
            
            var updatedIndex = entries.ToDictionary(x => x.Key, x => x.Value.Item2);
            _keyOffsetCache.Reset(updatedIndex);
        }
    }
}
