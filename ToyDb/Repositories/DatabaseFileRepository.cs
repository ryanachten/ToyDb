using System.Threading;
using ToyDb.Models;
using ToyDb.Services.AppendOnlyLogService;

namespace ToyDb.Repositories
{
    public class DatabaseFileRepository : IDatabaseRepository
    {
        /// <summary>
        /// Inmemory key - log offset lookup
        /// </summary>
        private readonly Dictionary<string, long> _index = [];
        private readonly IAppendOnlyLogService _appendOnlyLogService;

        public DatabaseFileRepository(IAppendOnlyLogService appendOnlyLogService)
        {
            _appendOnlyLogService = appendOnlyLogService;
            RestoreIndexFromFile(); // TODO: find a better way to call this, doesn't work
        }

        public async Task<DatabaseEntry> GetValue(string key, CancellationToken cancellationToken)
        {
            var hasOffset = _index.TryGetValue(key, out var offset);
            
            if (!hasOffset) return DatabaseEntry.Empty();

            return await _appendOnlyLogService.Read(offset, cancellationToken);
        }

        public async Task<Dictionary<string, DatabaseEntry>> GetValues(CancellationToken cancellationToken)
        {
            var entries = await _appendOnlyLogService.ReadAll(cancellationToken);
            return entries.ToDictionary((x) => x.Key, (x) => x.Value.Item1);
        }

        public async Task<DatabaseEntry> SetValue(string key, DatabaseEntry value, CancellationToken cancellationToken)
        {
            var offset = await _appendOnlyLogService.Append(key, value, cancellationToken);
            
            _index[key] = offset;
            
            return value;
        }

        private async Task RestoreIndexFromFile()
        {
            var entries = await _appendOnlyLogService.ReadAll(CancellationToken.None);
            foreach (var item in entries)
            {
                _index.Add(item.Key, item.Value.Item2);
            }
        }
    }
}
