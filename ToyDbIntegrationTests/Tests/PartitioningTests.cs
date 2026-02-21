using ToyDbClient.Clients;
using ToyDbRouting.Clients;
using ToyDbIntegrationTests.Helpers;

namespace ToyDbIntegrationTests.Tests;

public class PartitioningTests
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

    public PartitioningTests()
    {
        _routingClient = new RoutingClient(RoutingAddress, IntegrationTestConfig.SkipCertificateValidation);
        _p1r1Client = new ReplicaClient(P1R1Address);
        _p1r2Client = new ReplicaClient(P1R2Address);
        _p2r1Client = new ReplicaClient(P2R1Address);
        _p2r2Client = new ReplicaClient(P2R2Address);
    }

    [Fact]
    public async Task GivenMultipleKeys_WhenSetValue_ThenKeysDistributeAcrossPartitions()
    {
        var keys = TestDataGenerator.CreateTestKeys(20, "partition_dist");

        foreach (var key in keys)
        {
            var value = TestDataGenerator.CreateRandomValue();
            await _routingClient.SetValue(key, value);
        }

        var p1AllValues = await _p1r1Client.GetAllValuesAsDictionary();
        var p2AllValues = await _p2r1Client.GetAllValuesAsDictionary();

        var keysInP1 = keys.Where(p1AllValues.ContainsKey).ToList();
        var keysInP2 = keys.Where(p2AllValues.ContainsKey).ToList();

        Assert.NotEmpty(keysInP1);
        Assert.NotEmpty(keysInP2);
        Assert.Equal(keys.Count, keysInP1.Count + keysInP2.Count);
    }

    [Fact]
    public async Task GivenSameKey_WhenSetValue_ThenKeyRoutesToSamePartition()
    {
        var key = TestDataGenerator.CreateTestKey("same_partition");

        var value1 = "first_value";
        var value2 = "second_value";
        var value3 = "third_value";

        await _routingClient.SetValue(key, value1);
        await _routingClient.SetValue(key, value2);
        await _routingClient.SetValue(key, value3);

        var p1AllValues = await _p1r1Client.GetAllValuesAsDictionary();
        var p2AllValues = await _p2r1Client.GetAllValuesAsDictionary();

        var inP1 = p1AllValues.ContainsKey(key);
        var inP2 = p2AllValues.ContainsKey(key);

        Assert.True(inP1 ^ inP2); // Use XOR to ensure that values only exist in one partition, not both

        if (inP1)
        {
            Assert.Equal(value3, p1AllValues[key]);
        }
        else
        {
            Assert.Equal(value3, p2AllValues[key]);
        }
    }

    [Fact]
    public async Task GivenKeysInDifferentPartitions_WhenGetAllValues_ThenAllKeysReturned()
    {
        var keys = TestDataGenerator.CreateTestKeys(20, "all_values");
        var expectedValues = new Dictionary<string, string>();

        foreach (var key in keys)
        {
            var value = TestDataGenerator.CreateRandomValue();
            await _routingClient.SetValue(key, value);
            expectedValues[key] = value;
        }

        var allValues = await _routingClient.GetAllValues();

        foreach (var key in keys)
        {
            Assert.True(allValues.ContainsKey(key));
            Assert.Equal(expectedValues[key], allValues[key]);
        }
    }
}
