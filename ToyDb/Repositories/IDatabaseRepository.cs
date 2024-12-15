using ToyDb.Models;

namespace ToyDb.Repositories
{
    public interface IDatabaseRepository
    {
        DatabaseEntry GetValue(string key);
        Dictionary<string, DatabaseEntry> GetValues();
        DatabaseEntry SetValue(string key, DatabaseEntry value);
    }
}