using Microsoft.Extensions.Caching.Memory;

namespace ToyDb.Caches;

public class KeyOffsetCache : IKeyOffsetCache
{
    // TODO: currently all keys/offsets need to be kept in memory so no cache size limit has been set.
    // This feels like a scalability issue, though I know some DBs also have the same approach
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public bool TryGetValue(string key, out long value)
    {
        var hasValue = _cache.TryGetValue(key, out long cachedValue);

        value = cachedValue;

        return hasValue;
    }

    public void Set(string key, long? value) => _cache.Set(key, value);

    public void Remove(string key) => _cache.Remove(key);

    /// <summary>
    /// Replace the cache with a new state
    /// </summary>
    /// <param name="entries">Entries to represent new cache state</param>
    public void Replace(Dictionary<string, long> entries)
    {
        _cache.Clear();

        foreach (var entry in entries)
        {
            _cache.Set(entry.Key, entry.Value);
        }
    }
}
