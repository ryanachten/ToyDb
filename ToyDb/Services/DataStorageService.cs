using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;

namespace ToyDb.Services
{
    public class DataStorageService : IDataStorageService
    {
        /// <summary>
        /// Inmemory key - log offset lookup
        /// </summary>
        private readonly Dictionary<string, long> _index = [];
        private readonly IDataStoreRepository _storeService;
        private readonly IWriteAheadLogRepository _walService;

        public DataStorageService(IDataStoreRepository storeService, IWriteAheadLogRepository walService)
        {
            _storeService = storeService;
            _walService = walService;

            RestoreIndexFromStore();
        }

        public DatabaseEntry GetValue(string key)
        {
            var hasOffset = _index.TryGetValue(key, out var offset);

            if (!hasOffset) return DatabaseEntry.Empty();

            return _storeService.GetValue(offset);
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

            _index[key] = offset;

            return value;
        }

        /// <summary>
        /// Restores database index from data store
        /// </summary>
        private void RestoreIndexFromStore()
        {
            var entries = _storeService.GetLatestEntries();
            foreach (var item in entries)
            {
                _index.Add(item.Key, item.Value.Item2);
            }
        }
    }
}
