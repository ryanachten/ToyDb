using ToyDb.Models;

namespace ToyDb.Repositories
{
    public interface IDatabaseRepository
    {
        Task<DatabaseEntry> GetValue(string key, CancellationToken cancellationToken);
        Task<Dictionary<string, DatabaseEntry>> GetValues(CancellationToken cancellationToken);
        Task<DatabaseEntry> SetValue(string key, DatabaseEntry value, CancellationToken cancellationToken);
    }
}