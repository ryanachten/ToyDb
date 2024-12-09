using System.Collections.Concurrent;

namespace ToyDb.Repositories;

public class DatabaseRepository : IDatabaseRepository
{
    private readonly ConcurrentDictionary<string, string?> _data = new();

    public string? GetValue(string key)
    {
        var hasValue = _data.TryGetValue(key, out string? value);
        return hasValue ? value : null;
    }

    public Dictionary<string, string?> GetValues()
    {
        return _data.ToDictionary();
    }

    public string? SetValue(string key, string value)
    {
        return _data.AddOrUpdate(key, (key) => value, (key, existingValue) => value); // TODO: concurrency issues here likely need much more thought. As it stands, last write will presumably win
    }
}
