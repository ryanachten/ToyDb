using Grpc.Core;
using Grpc.Net.Client;
using ToyDbClient.Clients;
using ToyDbContracts.Election;
using ToyDbIntegrationTests.Helpers;
using ToyDbRouting.Clients;

namespace ToyDbIntegrationTests.Tests;

public class ElectionTests(RoutingClient _routingClient)
{
    private const string RoutingAddress = "https://localhost:8081";
    private const string P1R1Address = "https://localhost:8083";
    private const string P1R2Address = "https://localhost:8085";
    private const string P1R3Address = "https://localhost:8091";
    private const string P2R1Address = "https://localhost:8087";
    private const string P2R2Address = "https://localhost:8089";
    private const string P2R3Address = "https://localhost:8093";

    private static GrpcChannel CreateInsecureChannel(string address)
    {
        var handler = new System.Net.Http.HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
    }

    [Fact]
    public async Task GivenWriteToPrimary_WhenSecondaryReads_ThenValueIsReplicated()
    {
        var key = TestDataGenerator.CreateTestKey("election_replication_test");
        var expectedValue = TestDataGenerator.CreateRandomValue();

        await _routingClient.SetValue(key, expectedValue);
        await Task.Delay(1000);

        var p1r1Client = new ReplicaClient(P1R1Address);
        var p1r2Client = new ReplicaClient(P1R2Address);
        var p1r3Client = new ReplicaClient(P1R3Address);

        var valueFromR1 = await p1r1Client.GetAndDeserializeValue<string>(key);
        var valueFromR2 = await p1r2Client.GetAndDeserializeValue<string>(key);
        var valueFromR3 = await p1r3Client.GetAndDeserializeValue<string>(key);

        var partition1HasKey = valueFromR1 != null;
        var partition2HasKey = valueFromR2 != null || valueFromR3 != null;

        if (partition1HasKey)
        {
            Assert.Equal(expectedValue, valueFromR1);
            Assert.Equal(expectedValue, valueFromR2);
            Assert.Equal(expectedValue, valueFromR3);
        }
        else
        {
            var p2r1Client = new ReplicaClient(P2R1Address);
            var p2r2Client = new ReplicaClient(P2R2Address);
            var p2r3Client = new ReplicaClient(P2R3Address);

            var valueFromP2R1 = await p2r1Client.GetAndDeserializeValue<string>(key);
            var valueFromP2R2 = await p2r2Client.GetAndDeserializeValue<string>(key);
            var valueFromP2R3 = await p2r3Client.GetAndDeserializeValue<string>(key);

            Assert.Equal(expectedValue, valueFromP2R1);
            Assert.Equal(expectedValue, valueFromP2R2);
            Assert.Equal(expectedValue, valueFromP2R3);
        }
    }

    [Fact]
    public async Task GivenSecondaryNodes_WhenVoteRequested_ThenVoteIsGrantedForValidCandidate()
    {
        using var channel = CreateInsecureChannel(P1R2Address);

        var client = new Election.ElectionClient(channel);

        var request = new RequestVoteRequest
        {
            Term = 9999,
            NodeId = "test-candidate",
            LastLsn = 1
        };

        var response = await client.RequestVoteAsync(request);

        Assert.NotNull(response);
        Assert.True(response.Granted);
        Assert.Equal(9999, response.Term);
    }

    [Fact]
    public async Task GivenHeartbeatSent_WhenTermIsValid_ThenHeartbeatIsAccepted()
    {
        using var channel = CreateInsecureChannel(P1R2Address);

        var client = new Election.ElectionClient(channel);

        var request = new HeartbeatRequest
        {
            Term = 9999,
            LeaderId = "test-leader",
            CommitLsn = 0
        };

        var response = await client.HeartbeatAsync(request);

        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task GivenMultipleWrites_WhenReadAfterDelay_ThenAllReplicasHaveConsistentData()
    {
        var key = TestDataGenerator.CreateTestKey("election_consistency_test");
        var values = new[] { "value1", "value2", "value3" };

        foreach (var value in values)
        {
            await _routingClient.SetValue(key, value);
            await Task.Delay(200);
        }

        await Task.Delay(1000);

        var p1r1Client = new ReplicaClient(P1R1Address);
        var p1r2Client = new ReplicaClient(P1R2Address);
        var p1r3Client = new ReplicaClient(P1R3Address);
        var p2r1Client = new ReplicaClient(P2R1Address);
        var p2r2Client = new ReplicaClient(P2R2Address);
        var p2r3Client = new ReplicaClient(P2R3Address);

        var finalValue = values[^1];

        var valueFromP1R1 = await p1r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP1R2 = await p1r2Client.GetAndDeserializeValue<string>(key);
        var valueFromP1R3 = await p1r3Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R1 = await p2r1Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R2 = await p2r2Client.GetAndDeserializeValue<string>(key);
        var valueFromP2R3 = await p2r3Client.GetAndDeserializeValue<string>(key);

        var partition1Values = new[] { valueFromP1R1, valueFromP1R2, valueFromP1R3 };
        var partition2Values = new[] { valueFromP2R1, valueFromP2R2, valueFromP2R3 };

        var partition1HasKey = partition1Values.Any(v => v != null);
        var partition2HasKey = partition2Values.Any(v => v != null);

        Assert.True(partition1HasKey || partition2HasKey);
        Assert.True(partition1HasKey != partition2HasKey);

        if (partition1HasKey)
        {
            Assert.All(partition1Values, v => Assert.Equal(finalValue, v));
        }
        else
        {
            Assert.All(partition2Values, v => Assert.Equal(finalValue, v));
        }
    }

    [Fact]
    public async Task GivenNonPrimaryNode_WhenWriteAttempted_ThenWriteSucceeds()
    {
        var p1r2Client = new ReplicaClient(P1R2Address);
        var key = TestDataGenerator.CreateTestKey("non_primary_write_test");
        var keyValuePair = ToyDbIntegrationTests.Helpers.DataSerializer.Serialize(key, "test_value");

        var response = await p1r2Client.SetValue(keyValuePair);

        Assert.NotNull(response);
        Assert.Equal(key, response.Key);
    }
}
