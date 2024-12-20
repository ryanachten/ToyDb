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
        private readonly IDataStoreRepository _storeService;
        private readonly IWriteAheadLogRepository _walService;

        public DataStorageService(
            IKeyOffsetCache keyOffsetCache,
            IKeyEntryCache keyEntryCache,
            IDataStoreRepository storeService,
            IWriteAheadLogRepository walService)
        {
            _keyOffsetCache = keyOffsetCache;
            _keyEntryCache = keyEntryCache;
            _storeService = storeService;
            _walService = walService;

            RestoreIndexFromStore();
        }

        public void CompactLogs()
        {
            var entities = _storeService.GetLatestEntries().Select(x => x.Value.Item1);
            _storeService.CreateNewLogFile();

            var updatedIndex = _storeService.AppendRange(entities);
            _keyOffsetCache.Reset(updatedIndex);
        }

        public DatabaseEntry GetValue(string key)
        {
            var hasEntry = _keyEntryCache.TryGetValue(key, out var cachedEntry);
            if (hasEntry && cachedEntry != null) return cachedEntry;
            if (hasEntry && cachedEntry == null) return DatabaseEntry.Empty(key);

            var hasOffset = _keyOffsetCache.TryGetValue(key, out long offset);
            if (!hasOffset) return DatabaseEntry.Empty(key);

            var storedEntry = _storeService.GetValue(offset);
            
            _keyEntryCache.Set(key, storedEntry);

            return storedEntry;
        }

        public Dictionary<string, DatabaseEntry> GetValues()
        {
            var entries = _storeService.GetLatestEntries();
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
            _walService.Append(key, value);
            var offset = _storeService.Append(key, value);

            _keyOffsetCache.Set(key, offset);

            return value;
        }

        /// <summary>
        /// Restores database index from data store
        /// </summary>
        private void RestoreIndexFromStore()
        {
            var entries = _storeService.GetLatestEntries();
            
            var updatedIndex = entries.ToDictionary(x => x.Key, x => x.Value.Item2);
            _keyOffsetCache.Reset(updatedIndex);
        }
    }
}
