using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using ToyDb.Models;
using ToyDb.Services;
using ToyDbContracts.Data;

namespace ToyDbUnitTests.Services;

public class ReplicationLogNotifierTests
{
    [Fact]
    public void GivenPublishedEntry_WhenReading_ThenEntryIsReceived()
    {
        var notifier = new ReplicationLogNotifier();
        var entry = CreateWalEntry(1, "test-key", false);

        notifier.Publish(entry);

        var received = notifier.ReadAllAsync(CancellationToken.None).ToBlockingEnumerable().First();

        Assert.Equal(entry.Lsn, received.Lsn);
        Assert.Equal(entry.Key, received.Key);
        Assert.Equal(entry.IsDelete, received.IsDelete);
    }

    [Fact]
    public async Task GivenMultiplePublishedEntries_WhenReading_ThenAllEntriesAreReceived()
    {
        var notifier = new ReplicationLogNotifier();
        var entries = new[]
        {
            CreateWalEntry(1, "key1", false),
            CreateWalEntry(2, "key2", false),
            CreateWalEntry(3, "key3", true)
        };

        foreach (var entry in entries)
        {
            notifier.Publish(entry);
        }

        var received = new List<WalEntry>();
        await foreach (var entry in notifier.ReadAllAsync(CancellationToken.None))
        {
            received.Add(entry);
            if (received.Count >= entries.Length)
                break;
        }

        Assert.Equal(entries.Length, received.Count);
        Assert.Equal(entries[0].Lsn, received[0].Lsn);
        Assert.Equal(entries[1].Lsn, received[1].Lsn);
        Assert.Equal(entries[2].Lsn, received[2].Lsn);
    }

    [Fact]
    public async Task GivenNoPublishedEntries_WhenReading_ThenWaitsForEntry()
    {
        var notifier = new ReplicationLogNotifier();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var readTask = Task.Run(async () =>
        {
            await foreach (var _ in notifier.ReadAllAsync(cts.Token))
            {
                return true;
            }
            return false;
        });

        await Task.Delay(50);
        notifier.Publish(CreateWalEntry(1, "test-key", false));

        var result = await readTask;

        Assert.True(result);
    }

    [Fact]
    public async Task GivenCancellation_WhenReading_ThenStopsReading()
    {
        var notifier = new ReplicationLogNotifier();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var received = new List<WalEntry>();
        try
        {
            await foreach (var entry in notifier.ReadAllAsync(cts.Token))
            {
                received.Add(entry);
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Empty(received);
    }

    private static WalEntry CreateWalEntry(long lsn, string key, bool isDelete)
    {
        return new WalEntry
        {
            Lsn = lsn,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Key = key,
            Type = DataType.String,
            Data = ByteString.CopyFromUtf8("test-value"),
            IsDelete = isDelete
        };
    }
}
