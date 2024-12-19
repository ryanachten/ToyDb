using ToyDb.Models;

namespace ToyDb.Repositories.WriteAheadLogRepository;

public interface IWriteAheadLogRepository
{
    long Append(string key, DatabaseEntry entry, CancellationToken cancellationToken);
    Dictionary<string, (DatabaseEntry, long)> ReadAll(CancellationToken cancellationToken);
}