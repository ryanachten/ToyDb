using System.Collections.Concurrent;
using ToyDb.Caches;
using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;

namespace ToyDb.Services
{
    public sealed class WriteStorageService : IWriteStorageService, IDisposable
    {
        private readonly IKeyOffsetCache _keyOffsetCache;
        private readonly IKeyEntryCache _keyEntryCache;
        private readonly IDataStoreRepository _storeRepository;
        private readonly IWriteAheadLogRepository _walRepository;
        private readonly ILogger<WriteStorageService> _logger;

        /// <summary>
        /// Writes are queued to prevent lock contention and avoid out of order operations
        /// </summary>
        private readonly ConcurrentQueue<Task> _writeQueue = new();
        private readonly SemaphoreSlim _writeSemaphore = new(0);
        private readonly CancellationTokenSource _writeCancellationToken = new();

        public WriteStorageService(
            IKeyOffsetCache keyOffsetCache,
            IKeyEntryCache keyEntryCache,
            IDataStoreRepository storeRepository,
            IWriteAheadLogRepository walRepository,
            ILogger<WriteStorageService> logger)
        {
            _keyOffsetCache = keyOffsetCache;
            _keyEntryCache = keyEntryCache;
            _storeRepository = storeRepository;
            _walRepository = walRepository;
            _logger = logger;

            Task.Run(() => PollWriteQueue());
        }

        /// <summary>
        /// Write value to both write-ahead log and data store
        /// </summary>
        /// <param name="key">Key to assign value to</param>
        /// <param name="value">Value to assign to key</param>
        public Task SetValue(string key, DatabaseEntry value) => EnqueueWrite(() => ExecuteSetValue(key, value));

        /// <summary>
        /// Deletes values by appending tombstones to logs and clearing cache entries
        /// Tombstones will be cleaned up during log compaction
        /// </summary>
        /// <param name="key">Key of value to be deleted</param>
        public Task DeleteValue(string key) => EnqueueWrite(() => ExecuteDeleteValue(key));

        /// <summary>
        /// Compacts logs by storing only the latest entries in a new file
        /// </summary>
        public Task CompactLogs() => EnqueueWrite(() => ExecuteCompactLogs());

        private void ExecuteSetValue(string key, DatabaseEntry value)
        {
            _walRepository.Append(key, value);
            var offset = _storeRepository.Append(key, value);

            _keyOffsetCache.Set(key, offset);
            _keyEntryCache.Set(key, value);
        }

        private void ExecuteDeleteValue(string key)
        {
            // Don't commit deletes to values which don't exist
            if (!_keyOffsetCache.TryGetValue(key, out long _)) return;

            var value = DatabaseEntry.Null(key);

            _walRepository.Append(key, value);
            _storeRepository.Append(key, value);

            _keyOffsetCache.Remove(key);
            _keyEntryCache.Remove(key);
        }

        private void ExecuteCompactLogs()
        {
            if (!_storeRepository.HasRedundantData()) return;

            var entities = _storeRepository.GetLatestEntries().Select(x => x.Value.Item1);

            _storeRepository.CreateNewLogFile();

            var updatedIndex = _storeRepository.AppendRange(entities);
            _keyOffsetCache.Replace(updatedIndex);
        }

        private Task EnqueueWrite(Action action)
        {
            var task = new Task(action);

            _writeQueue.Enqueue(task);
            _writeSemaphore.Release(); // Signal that a write is available

            return task;
        }

        /// <summary>
        /// Creates a thread which watches for write operations in the queue
        /// </summary>
        private async Task PollWriteQueue()
        {
            while (!_writeCancellationToken.Token.IsCancellationRequested)
            {
                await _writeSemaphore.WaitAsync(); // Waits for writes to be present in queue

                if (_writeQueue.TryDequeue(out var writeTask) && writeTask != null)
                {
                    try
                    {
                        writeTask.Start();
                        await writeTask.WaitAsync(_writeCancellationToken.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Write failed to process: {Exception}", ex);
                    }
                }
            }
        }

        public void Dispose() => _writeCancellationToken.Dispose();
    }
}
