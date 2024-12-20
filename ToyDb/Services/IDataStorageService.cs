using ToyDb.Models;

namespace ToyDb.Services
{
    public interface IDataStorageService
    {
        DatabaseEntry GetValue(string key);
        Dictionary<string, DatabaseEntry> GetValues();
        DatabaseEntry SetValue(string key, DatabaseEntry value);
        void CompactLogs();
    }
}