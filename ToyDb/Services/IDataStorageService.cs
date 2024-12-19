using ToyDb.Models;

namespace ToyDb.Services
{
    public interface IDataStorageService
    {
        DatabaseEntry GetValue(string key, CancellationToken cancellationToken);
        Dictionary<string, DatabaseEntry> GetValues(CancellationToken cancellationToken);
        DatabaseEntry SetValue(string key, DatabaseEntry value, CancellationToken cancellationToken);
    }
}