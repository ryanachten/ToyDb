using ToyDbClient.Clients;
using ToyDbRouting.Clients;
using ToyDbIntegrationTests.Helpers;
using ToyDbContracts.Data;

namespace ToyDbIntegrationTests.Tests;

public class ReplicationLogTests
{
    private const string RoutingAddress = "https://localhost:8081";
    private const string P1R1Address = "https://localhost:8083";
    private const string P1R2Address = "https://localhost:8085";
    private const string P2R1Address = "https://localhost:8087";
    private const string P2R2Address = "https://localhost:8089";

    private readonly RoutingClient _routingClient;
    private readonly ReplicaClient _p1r1Client;
    private readonly ReplicaClient _p1r2Client;
    private readonly ReplicaClient _p2r1Client;
    private readonly ReplicaClient _p2r2Client;

    public ReplicationLogTests()
    {
        _routingClient = new RoutingClient(RoutingAddress, IntegrationTestConfig.SkipCertificateValidation);
        _p1r1Client = new ReplicaClient(P1R1Address);
        _p1r2Client = new ReplicaClient(P1R2Address);
        _p2r1Client = new ReplicaClient(P2R1Address);
        _p2r2Client = new ReplicaClient(P2R2Address);
    }

    [Fact]
    public async Task GivenValueWrittenViaRouting_WhenStreamingReplicationLogFromPrimary_ThenEntryAppears()
    {
        var (key, expectedValue) = TestDataGenerator.CreateTestKeyValue("stream_test");
        await _routingClient.SetValue(key, expectedValue);
        await Task.Delay(500);

        var primaryClient = await DeterminePrimaryForKey(key);
        var entries = new List<ReplicationLogEntry>();

        await foreach (var entry in primaryClient.StreamReplicationLog(0))
        {
            entries.Add(entry);
            if (entry.Key == key)
            {
                break;
            }
        }

        var matchingEntry = entries.FirstOrDefault(e => e.Key == key);
        Assert.NotNull(matchingEntry);
        Assert.Equal(expectedValue, matchingEntry.Value.ToStringUtf8());
        Assert.False(matchingEntry.IsDelete);
        Assert.True(matchingEntry.Lsn > 0);
    }

    [Fact]
    public async Task GivenMultipleWritesViaRouting_WhenStreamingReplicationLog_ThenLsnOrderingIsMaintained()
    {
        var keys = TestDataGenerator.CreateTestKeys(5, "lsn_order");
        var values = keys.Select(k => TestDataGenerator.CreateRandomValue()).ToList();

        for (int i = 0; i < keys.Count; i++)
        {
            await _routingClient.SetValue(keys[i], values[i]);
        }
        await Task.Delay(500);

        var primaryClient = await DeterminePrimaryForKey(keys[0]);
        var keysOnPrimary = await GetKeysOnClient(keys, primaryClient);

        var entries = new List<ReplicationLogEntry>();

        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        await foreach (var entry in primaryClient.StreamReplicationLog(0, cts.Token))
        {
            if (keysOnPrimary.Contains(entry.Key))
            {
                entries.Add(entry);
            }

            if (entries.Count == keysOnPrimary.Count)
            {
                break;
            }
        }

        Assert.True(keysOnPrimary.Count > 0, "At least one key should be on the primary partition");
        Assert.Equal(keysOnPrimary.Count, entries.Count);

        for (int i = 0; i < entries.Count - 1; i++)
        {
            Assert.True(entries[i].Lsn < entries[i + 1].Lsn,
                $"LSN {entries[i].Lsn} should be less than {entries[i + 1].Lsn}");
        }
    }

    [Fact]
    public async Task GivenWritesViaRouting_WhenReadingFromDataStore_ThenDataStoreIsConsistentWithWal()
    {
        var keys = TestDataGenerator.CreateTestKeys(3, "consistency");
        var values = keys.Select(k => TestDataGenerator.CreateRandomValue()).ToList();

        for (int i = 0; i < keys.Count; i++)
        {
            await _routingClient.SetValue(keys[i], values[i]);
        }
        await Task.Delay(500);

        var primaryClient = await DeterminePrimaryForKey(keys[0]);
        var keysOnPrimary = await GetKeysOnClient(keys, primaryClient);

        var walEntries = new List<ReplicationLogEntry>();
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        await foreach (var entry in primaryClient.StreamReplicationLog(0, cts.Token))
        {
            if (keysOnPrimary.Contains(entry.Key))
            {
                walEntries.Add(entry);
            }

            if (walEntries.Count == keysOnPrimary.Count)
            {
                break;
            }
        }

        Assert.True(keysOnPrimary.Count > 0, "At least one key should be on the primary partition");
        Assert.Equal(keysOnPrimary.Count, walEntries.Count);

        var dataStoreValues = new Dictionary<string, string>();
        foreach (var key in keysOnPrimary)
        {
            var value = await primaryClient.GetAndDeserializeValue<string>(key);
            if (value != null)
            {
                dataStoreValues[key] = value;
            }
        }

        Assert.Equal(keysOnPrimary.Count, dataStoreValues.Count);

        foreach (var walEntry in walEntries)
        {
            if (!walEntry.IsDelete)
            {
                Assert.True(dataStoreValues.ContainsKey(walEntry.Key),
                    $"Data store should contain key {walEntry.Key} from WAL");
                var expectedValue = walEntry.Value.ToStringUtf8();
                Assert.Equal(expectedValue, dataStoreValues[walEntry.Key]);
            }
        }
    }

    private async Task<HashSet<string>> GetKeysOnClient(List<string> keys, ReplicaClient client)
    {
        var result = new HashSet<string>();
        foreach (var key in keys)
        {
            var value = await client.GetAndDeserializeValue<string>(key);
            if (value != null)
            {
                result.Add(key);
            }
        }
        return result;
    }

    private async Task<ReplicaClient> DeterminePrimaryForKey(string key)
    {
        await Task.Delay(100);

        var valueP1R1 = await _p1r1Client.GetAndDeserializeValue<string>(key);
        var valueP2R1 = await _p2r1Client.GetAndDeserializeValue<string>(key);

        if (valueP1R1 != null)
        {
            return _p1r1Client;
        }

        if (valueP2R1 != null)
        {
            return _p2r1Client;
        }

        throw new InvalidOperationException($"Could not determine primary for key {key}");
    }
}
