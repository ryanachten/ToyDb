using ToyDb.Models;

namespace ToyDb.Repositories.WriteAheadLogRepository;

public interface IWriteAheadLogRepository
{
    long Append(string key, DatabaseEntry entry);
    Dictionary<string, (DatabaseEntry, long)> GetLatestEntries();
}