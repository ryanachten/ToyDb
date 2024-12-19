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

        public DatabaseEntry GetValue(string key, CancellationToken cancellationToken)
        {
            var hasOffset = _index.TryGetValue(key, out var offset);

            if (!hasOffset) return DatabaseEntry.Empty();

            return _storeService.Read(offset, cancellationToken);
        }

        public Dictionary<string, DatabaseEntry> GetValues(CancellationToken cancellationToken)
        {
            var entries = _storeService.ReadAll(cancellationToken);
            return entries.ToDictionary((x) => x.Key, (x) => x.Value.Item1);
        }

        /// <summary>
        /// Write value to both write-ahead log and data store
        /// </summary>
        /// <param name="key">Key to assign value to</param>
        /// <param name="value">Value to assign to key</param>
        /// <param name="cancellationToken">Token to cancel operation</param>
        /// <returns>Saved database entry</returns>
        public DatabaseEntry SetValue(string key, DatabaseEntry value, CancellationToken cancellationToken)
        {
            _walService.Append(key, value, cancellationToken);
            var offset = _storeService.Append(key, value, cancellationToken);

            _index[key] = offset;

            return value;
        }

        /// <summary>
        /// Restores database index from data store
        /// </summary>
        private void RestoreIndexFromStore()
        {
            var entries = _storeService.ReadAll(CancellationToken.None);
            foreach (var item in entries)
            {
                _index.Add(item.Key, item.Value.Item2);
            }
        }
    }
}
