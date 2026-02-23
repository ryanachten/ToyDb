using ToyDb.Models;

namespace ToyDb.Repositories.WriteAheadLogRepository;

public interface IWriteAheadLogRepository
{
    long Append(long lsn, string key, DatabaseEntry entry, bool isDelete);
    IEnumerable<WalEntry> ReadFrom(long fromLsn);
    long GetLatestLsn();
}