using ToyDb.Caches;
using ToyDb.Extensions;
using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;

namespace ToyDb.Services;

public class WalRecoveryService(
    IDataStoreRepository storeRepository,
    IWriteAheadLogRepository walRepository,
    IKeyOffsetCache keyOffsetCache,
    IKeyEntryCache keyEntryCache,
    ILogger<WalRecoveryService> logger)
{
    public void Recover()
    {
        var timer = logger.StartTimedLog(nameof(Recover));

        var dataStoreLatestLsn = storeRepository.GetLatestLsn();
        logger.LogInformation("Data store latest LSN: {LatestLsn}", dataStoreLatestLsn);

        var walEntriesToReplay = walRepository.ReadFrom(dataStoreLatestLsn + 1).ToList();
        logger.LogInformation("Found {Count} WAL entries to replay", walEntriesToReplay.Count);

        foreach (var walEntry in walEntriesToReplay)
        {
            var databaseEntry = new DatabaseEntry
            {
                Timestamp = walEntry.Timestamp,
                Key = walEntry.Key,
                Type = walEntry.Type,
                Data = walEntry.Data
            };

            var offset = storeRepository.Append(walEntry.Lsn, walEntry.Key, databaseEntry, walEntry.IsDelete);

            if (walEntry.IsDelete)
            {
                keyOffsetCache.Remove(walEntry.Key);
                keyEntryCache.Remove(walEntry.Key);
            }
            else
            {
                keyOffsetCache.Set(walEntry.Key, offset);
                keyEntryCache.Set(walEntry.Key, databaseEntry);
            }
        }

        var entries = storeRepository.GetLatestEntries();
        var updatedIndex = entries.ToDictionary(x => x.Key, x => x.Value.Item2);
        keyOffsetCache.Replace(updatedIndex);

        timer.Stop();
    }
}
