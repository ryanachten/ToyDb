using ToyDbClient.Clients;
using ToyDbRouting.Clients;
using ToyDbIntegrationTests.Helpers;

namespace ToyDbIntegrationTests.Tests;

public class ReplicationTests
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

    public ReplicationTests()
    {
        _routingClient = new RoutingClient(RoutingAddress, IntegrationTestConfig.SkipCertificateValidation);
        _p1r1Client = new ReplicaClient(P1R1Address);
        _p1r2Client = new ReplicaClient(P1R2Address);
        _p2r1Client = new ReplicaClient(P2R1Address);
        _p2r2Client = new ReplicaClient(P2R2Address);
    }

    [Fact]
    public async Task GivenKeyWrittenThroughRouting_WhenReadFromBothReplicas_ThenPrimaryAndSecondaryInSamePartitionHaveValue()
    {
        var key = TestDataGenerator.CreateTestKey("replication_test");
        var expectedValue = TestDataGenerator.CreateRandomValue();

        await _routingClient.SetValue(key, expectedValue);
        await Task.Delay(500);

        var valueFromP1R1 = await _p1r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP1R2 = await _p1r2Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R1 = await _p2r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R2 = await _p2r2Client.GetAndDeserializeValue<string>(key);

        var partition1HasKey = valueFromP1R1 != null && valueFromP1R2 != null;
        var partition2HasKey = valueFromP2R1 != null && valueFromP2R2 != null;

        Assert.True(partition1HasKey ^ partition2HasKey);

        if (partition1HasKey)
        {
            Assert.Equal(expectedValue, valueFromP1R1);
            Assert.Equal(expectedValue, valueFromP1R2);
            Assert.Null(valueFromP2R1);
            Assert.Null(valueFromP2R2);
        }
        else
        {
            Assert.Equal(expectedValue, valueFromP2R1);
            Assert.Equal(expectedValue, valueFromP2R2);
            Assert.Null(valueFromP1R1);
            Assert.Null(valueFromP1R2);
        }
    }

    [Fact]
    public async Task GivenKeyUpdatedMultipleTimes_WhenReadFromReplicas_ThenPrimaryAndSecondaryHaveFinalValue()
    {
        var key = TestDataGenerator.CreateTestKey("multi_update_test");
        var value1 = "value1";
        var value2 = "value2";
        var value3 = "value3";

        await _routingClient.SetValue(key, value1);
        await _routingClient.SetValue(key, value2);
        await _routingClient.SetValue(key, value3);
        await Task.Delay(500);

        var valueFromP1R1 = await _p1r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP1R2 = await _p1r2Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R1 = await _p2r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R2 = await _p2r2Client.GetAndDeserializeValue<string>(key);

        var partition1HasKey = valueFromP1R1 != null && valueFromP1R2 != null;
        var partition2HasKey = valueFromP2R1 != null && valueFromP2R2 != null;

        Assert.True(partition1HasKey ^ partition2HasKey);

        if (partition1HasKey)
        {
            Assert.Equal(value3, valueFromP1R1);
            Assert.Equal(value3, valueFromP1R2);
        }
        else
        {
            Assert.Equal(value3, valueFromP2R1);
            Assert.Equal(value3, valueFromP2R2);
        }
    }

    [Fact]
    public async Task GivenKeyDeleted_WhenReadFromAllReplicas_ThenNoReplicasHaveValue()
    {
        var key = TestDataGenerator.CreateTestKey("delete_replication_test");
        var value = TestDataGenerator.CreateRandomValue();

        await _routingClient.SetValue(key, value);
        await _routingClient.DeleteValue(key);
        await Task.Delay(500);

        var valueFromP1R1 = await _p1r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP1R2 = await _p1r2Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R1 = await _p2r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R2 = await _p2r2Client.GetAndDeserializeValue<string>(key);

        Assert.Null(valueFromP1R1);
        Assert.Null(valueFromP1R2);
        Assert.Null(valueFromP2R1);
        Assert.Null(valueFromP2R2);
    }

    [Fact]
    public async Task GivenDirectWriteToPrimary_WhenReadFromReplicas_ThenOnlyPrimaryHasValue() // TODO: I think this is a symptom of us handling replication in the wrong place
    {
        var key = TestDataGenerator.CreateTestKey("partition1_direct_write");
        var expectedValue = TestDataGenerator.CreateRandomValue();

        await _p1r1Client.SetAndDeserializeValue(key, expectedValue);
        await Task.Delay(500);

        var valueFromP1R1 = await _p1r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP1R2 = await _p1r2Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R1 = await _p2r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R2 = await _p2r2Client.GetAndDeserializeValue<string>(key);

        Assert.Equal(expectedValue, valueFromP1R1);
        Assert.Null(valueFromP1R2);
        Assert.Null(valueFromP2R1);
        Assert.Null(valueFromP2R2);
    }

    [Fact]
    public async Task GivenWriteThroughRouting_WhenComparedToDirectWrite_ThenRoutingReplicatesButDirectDoesNot() // TODO: I think this is a symptom of us handling replication in the wrong place
    {
        var keyThroughRouting = TestDataGenerator.CreateTestKey("routing_write");
        var keyDirectWrite = TestDataGenerator.CreateTestKey("direct_write");
        var value1 = TestDataGenerator.CreateRandomValue();
        var value2 = TestDataGenerator.CreateRandomValue();

        await _routingClient.SetValue(keyThroughRouting, value1);
        await _p1r1Client.SetAndDeserializeValue(keyDirectWrite, value2);
        await Task.Delay(500);

        var routingValueR1 = await _p1r1Client.GetAndDeserializeValue<string>(keyThroughRouting);
        var routingValueR2 = await _p1r2Client.GetAndDeserializeValue<string>(keyThroughRouting);
        var directValueR1 = await _p1r1Client.GetAndDeserializeValue<string>(keyDirectWrite);
        var directValueR2 = await _p1r2Client.GetAndDeserializeValue<string>(keyDirectWrite);

        if (routingValueR1 != null)
        {
            Assert.Equal(value1, routingValueR1);
            Assert.Equal(value1, routingValueR2);
        }

        Assert.Equal(value2, directValueR1);
        Assert.Null(directValueR2);
    }

}
