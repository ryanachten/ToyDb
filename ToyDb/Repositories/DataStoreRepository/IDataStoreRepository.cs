using ToyDb.Models;

namespace ToyDb.Repositories.DataStoreRepository;

public interface IDataStoreRepository
{
    long Append(string key, DatabaseEntry entry, CancellationToken cancellationToken);
    DatabaseEntry Read(long offset, CancellationToken cancellationToken);
    Dictionary<string, (DatabaseEntry, long)> ReadAll(CancellationToken cancellationToken);
}