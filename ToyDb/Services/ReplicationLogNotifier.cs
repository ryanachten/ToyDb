using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ToyDb.Models;

namespace ToyDb.Services;

public class ReplicationLogNotifier : IReplicationLogNotifier
{
    private readonly ConcurrentDictionary<Guid, ChannelWriter<WalEntry>> _subscribers = new();

    public void Publish(WalEntry entry)
    {
        foreach (var (id, writer) in _subscribers)
        {
            if (!writer.TryWrite(entry))
            {
                // Remove closed or full subscribers
                _subscribers.TryRemove(id, out _);
            }
        }
    }

    public async IAsyncEnumerable<WalEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<WalEntry>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel.Writer;

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
