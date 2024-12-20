using ToyDb.Models;

namespace ToyDb.Caches
{
    public interface IKeyEntryCache
    {
        void Set(string key, DatabaseEntry? value);
        bool TryGetValue(string key, out DatabaseEntry? value);
    }
}