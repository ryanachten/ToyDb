using ToyDb.Models;

namespace ToyDb.Services;

public interface IReplicationLogSubscription : IAsyncDisposable
{
    IAsyncEnumerable<WalEntry> ReadAllAsync(CancellationToken cancellationToken);
}
