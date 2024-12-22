namespace ToyDb.Caches
{
    public interface IKeyOffsetCache
    {
        bool TryGetValue(string key, out long value);
        void Replace(Dictionary<string, long> entries);
        void Set(string key, long? value);
        void Remove(string key);
    }
}