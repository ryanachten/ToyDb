using ToyDb.Models;

namespace ToyDb.Services
{
    public interface IDataStorageService
    {
        DatabaseEntry GetValue(string key);
        Dictionary<string, DatabaseEntry> GetValues();
        Task SetValue(string key, DatabaseEntry value);
        Task CompactLogs();
        Task DeleteValue(string key);
    }
}