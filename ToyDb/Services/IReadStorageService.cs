using ToyDb.Models;

namespace ToyDb.Services
{
    public interface IReadStorageService
    {
        DatabaseEntry GetValue(string key);
        Dictionary<string, DatabaseEntry> GetValues();
    }
}