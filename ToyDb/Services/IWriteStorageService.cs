using ToyDb.Models;

namespace ToyDb.Services
{
    public interface IWriteStorageService
    {
        Task SetValue(string key, DatabaseEntry value);
        Task CompactLogs();
        Task DeleteValue(string key);
    }
}