using System.Collections.Concurrent;
using ToyDb.Models;

namespace ToyDb.Repositories;

public class DatabaseRepository : IDatabaseRepository
{
    private readonly ConcurrentDictionary<string, DatabaseEntry> _data = new();

    public DatabaseEntry GetValue(string key)
    {
        var hasValue = _data.TryGetValue(key, out var value);
        return hasValue && value != null ? value : DatabaseEntry.Empty();
    }

    public Dictionary<string, DatabaseEntry> GetValues()
    {
        return _data.ToDictionary();
    }

    public DatabaseEntry SetValue(string key, DatabaseEntry value)
    {
        return _data.AddOrUpdate(key, (key) => value, (key, existingValue) => value); // TODO: concurrency issues here likely need much more thought. As it stands, last write will presumably win
    }
}
