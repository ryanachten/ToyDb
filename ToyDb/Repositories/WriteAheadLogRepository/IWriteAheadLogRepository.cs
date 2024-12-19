using ToyDb.Models;

namespace ToyDb.Repositories.WriteAheadLogRepository;

public interface IWriteAheadLogRepository
{
    Task<long> Append(string key, DatabaseEntry entry, CancellationToken cancellationToken);
    Task<Dictionary<string, (DatabaseEntry, long)>> ReadAll(CancellationToken cancellationToken);
}