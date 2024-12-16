using ToyDb.Models;

namespace ToyDb.Services.AppendOnlyLogService;

public interface IAppendOnlyLogService
{
    Task<long> Append(string key, DatabaseEntry entry, CancellationToken cancellationToken);
    Task<DatabaseEntry> Read(long offset, CancellationToken cancellationToken);
    Task<Dictionary<string, (DatabaseEntry, long)>> ReadAll(CancellationToken cancellationToken);
}