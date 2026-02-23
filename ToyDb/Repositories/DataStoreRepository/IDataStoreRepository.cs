using ToyDb.Models;

namespace ToyDb.Repositories.DataStoreRepository;

public interface IDataStoreRepository
{
    long Append(long lsn, string key, DatabaseEntry entry, bool isDelete);
    DatabaseEntry GetValue(long offset);
    Dictionary<string, (DatabaseEntry, long)> GetLatestEntries();
    Dictionary<string, (WalEntry, long)> GetLatestWalEntries();
    Dictionary<string, long> AppendRange(IEnumerable<WalEntry> entries);
    void CreateNewLogFile();
    bool HasRedundantData();
    long GetLatestLsn();
}