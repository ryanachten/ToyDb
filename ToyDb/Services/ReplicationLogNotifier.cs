using System.Threading.Channels;
using ToyDb.Models;

namespace ToyDb.Services;

public class ReplicationLogNotifier : IReplicationLogNotifier
{
    private readonly Channel<WalEntry> _channel = Channel.CreateUnbounded<WalEntry>();

    public void Publish(WalEntry entry)
    {
        _channel.Writer.TryWrite(entry);
    }

    public IAsyncEnumerable<WalEntry> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
