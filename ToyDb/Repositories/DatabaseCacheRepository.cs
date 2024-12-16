using System.Collections.Concurrent;
using ToyDb.Models;

namespace ToyDb.Repositories;

public class DatabaseCacheRepository : IDatabaseRepository
{
    private readonly ConcurrentDictionary<string, DatabaseEntry> _data = new();

    public Task<DatabaseEntry> GetValue(string key, CancellationToken cancellationToken)
    {
        var hasValue = _data.TryGetValue(key, out var value);
        var result = hasValue && value != null ? value : DatabaseEntry.Empty();
        return Task.FromResult(result);
    }

    public Task<Dictionary<string, DatabaseEntry>> GetValues(CancellationToken cancellationToken)
    {
        return Task.FromResult(_data.ToDictionary());
    }

    public Task<DatabaseEntry> SetValue(string key, DatabaseEntry value, CancellationToken cancellationToken)
    {
        var result = _data.AddOrUpdate(key, (key) => value, (key, existingValue) => value); // TODO: concurrency issues here likely need much more thought. As it stands, last write will presumably win
        return Task.FromResult(result);
    }
}
