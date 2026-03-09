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
                // Subscriber is full or closed — disconnect so it can reconnect from its last LSN
                if (_subscribers.TryRemove(id, out _))
                {
                    writer.TryComplete();
                }
            }
        }
    }

    public IReplicationLogSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<WalEntry>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleWriter = false,
            SingleReader = true
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel.Writer;

        return new ReplicationLogSubscription(id, channel.Reader, _subscribers);
    }

    private sealed class ReplicationLogSubscription : IReplicationLogSubscription
    {
        private readonly Guid _id;
        private readonly ChannelReader<WalEntry> _reader;
        private readonly ConcurrentDictionary<Guid, ChannelWriter<WalEntry>> _subscribers;

        public ReplicationLogSubscription(
            Guid id,
            ChannelReader<WalEntry> reader,
            ConcurrentDictionary<Guid, ChannelWriter<WalEntry>> subscribers)
        {
            _id = id;
            _reader = reader;
            _subscribers = subscribers;
        }

        public async IAsyncEnumerable<WalEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var entry in _reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_subscribers.TryRemove(_id, out var writer))
            {
                writer.TryComplete();
            }
            return ValueTask.CompletedTask;
        }
    }
}
