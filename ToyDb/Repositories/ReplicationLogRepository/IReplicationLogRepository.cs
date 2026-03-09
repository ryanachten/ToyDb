using ToyDb.Models;

namespace ToyDb.Repositories.ReplicationLogRepository;

public interface IReplicationLogRepository
{
    long Append(string key, DatabaseEntry entry);
    long GetLatestLsn();
    IEnumerable<ReplicationLogEntry> GetEntriesFromLsn(long fromLsn);
}
