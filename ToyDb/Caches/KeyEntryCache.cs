using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ToyDb.Models;

namespace ToyDb.Caches;

public class KeyEntryCache : IKeyEntryCache
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions()
    {
        SizeLimit = 1024, // In its current implementation, this will house max 1024 items in memory
    });
    private readonly MemoryCacheEntryOptions _entryOptions;

    public KeyEntryCache(IOptions<KeyEntryOptions> options)
    {
        _entryOptions = new()
        {
            Size = 1, // TODO: This treats all entries the same, which isn't optimal. Really we want to take the data size into consideration
            SlidingExpiration = TimeSpan.FromSeconds(options.Value.SlidingRetentionPeriod),
        };
    }

    public bool TryGetValue(string key, out DatabaseEntry? value)
    {
        var hasValue = _cache.TryGetValue(key, out DatabaseEntry? cachedValue);

        value = cachedValue;

        return hasValue;
    }

    public void Set(string key, DatabaseEntry? value) => _cache.Set(key, value, _entryOptions);
}
