namespace ToyDb.Caches
{
    public interface IKeyOffsetCache
    {
        bool TryGetValue(string key, out long value);
        void Reset(Dictionary<string, long> entries);
        void Set(string key, long? value);
    }
}