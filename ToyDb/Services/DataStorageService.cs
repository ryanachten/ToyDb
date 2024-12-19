using ToyDb.Models;
using ToyDb.Repositories;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;

namespace ToyDb.Services
{
    public class DataStorageService(IDataStoreRepository storeService, IWriteAheadLogRepository walService) : IDatabaseRepository
    {
        /// <summary>
        /// Inmemory key - log offset lookup
        /// </summary>
        private readonly Dictionary<string, long> _index = [];

        /// <summary>
        /// Restores database index from log file
        /// </summary>
        public async Task RestoreIndexFromFile()
        {
            var entries = await storeService.ReadAll(CancellationToken.None);
            foreach (var item in entries)
            {
                _index.Add(item.Key, item.Value.Item2);
            }
        }

        public async Task<DatabaseEntry> GetValue(string key, CancellationToken cancellationToken)
        {
            var hasOffset = _index.TryGetValue(key, out var offset);

            if (!hasOffset) return DatabaseEntry.Empty();

            return await storeService.Read(offset, cancellationToken);
        }

        public async Task<Dictionary<string, DatabaseEntry>> GetValues(CancellationToken cancellationToken)
        {
            var entries = await storeService.ReadAll(cancellationToken);
            return entries.ToDictionary((x) => x.Key, (x) => x.Value.Item1);
        }

        /// <summary>
        /// Write value to both write-ahead log and data store
        /// </summary>
        /// <param name="key">Key to assign value to</param>
        /// <param name="value">Value to assign to key</param>
        /// <param name="cancellationToken">Token to cancel operation</param>
        /// <returns>Saved database entry</returns>
        public async Task<DatabaseEntry> SetValue(string key, DatabaseEntry value, CancellationToken cancellationToken)
        {
            var walTask = walService.Append(key, value, cancellationToken);
            var storeTask = storeService.Append(key, value, cancellationToken);

            await Task.WhenAll(walTask, storeTask);

            _index[key] = storeTask.Result;

            return value;
        }
    }
}
