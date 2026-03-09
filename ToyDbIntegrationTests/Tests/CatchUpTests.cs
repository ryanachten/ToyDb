using ToyDbClient.Clients;
using ToyDbRouting.Clients;
using ToyDbIntegrationTests.Helpers;

namespace ToyDbIntegrationTests.Tests;

public class CatchUpTests
{
    private const string RoutingAddress = "https://localhost:8081";
    private const string P1R1Address = "https://localhost:8083";
    private const string P1R2Address = "https://localhost:8085";
    private const string P2R1Address = "https://localhost:8087";
    private const string P2R2Address = "https://localhost:8089";

    private const string P1R2Container = "toydb-p1-r2";
    private const string P2R2Container = "toydb-p2-r2";

    private readonly RoutingClient _routingClient;
    private readonly ReplicaClient _p1r1Client;
    private readonly ReplicaClient _p1r2Client;
    private readonly ReplicaClient _p2r1Client;
    private readonly ReplicaClient _p2r2Client;

    public CatchUpTests()
    {
        _routingClient = new RoutingClient(RoutingAddress, IntegrationTestConfig.SkipCertificateValidation);
        _p1r1Client = new ReplicaClient(P1R1Address);
        _p1r2Client = new ReplicaClient(P1R2Address);
        _p2r1Client = new ReplicaClient(P2R1Address);
        _p2r2Client = new ReplicaClient(P2R2Address);
    }

    [Fact]
    public async Task GivenSecondaryWasOfflineDuringWrite_WhenSecondaryRestarts_ThenSecondaryHasCaughtUpWithMissedWrites()
    {
        var initialKey = TestDataGenerator.CreateTestKey("catchup_initial");
        var initialValue = TestDataGenerator.CreateRandomValue();

        await _routingClient.SetValue(initialKey, initialValue);
        await Task.Delay(500);

        var valueFromP1R1 = await _p1r1Client.GetAndDeserializeValue<string>(initialKey);
        var isPartition1 = valueFromP1R1 != null;

        var primaryClient = isPartition1 ? _p1r1Client : _p2r1Client;
        var secondaryClient = isPartition1 ? _p1r2Client : _p2r2Client;
        var secondaryContainer = isPartition1 ? P1R2Container : P2R2Container;

        await DockerStop(secondaryContainer);

        var missedKey1 = TestDataGenerator.CreateTestKey("catchup_missed1");
        var missedValue1 = TestDataGenerator.CreateRandomValue();
        var missedKey2 = TestDataGenerator.CreateTestKey("catchup_missed2");
        var missedValue2 = TestDataGenerator.CreateRandomValue();

        await primaryClient.SetAndDeserializeValue(missedKey1, missedValue1);
        await primaryClient.SetAndDeserializeValue(missedKey2, missedValue2);

        await DockerStart(secondaryContainer);
        await Task.Delay(3000);

        var missed1FromSecondary = await secondaryClient.GetAndDeserializeValue<string>(missedKey1);
        var missed2FromSecondary = await secondaryClient.GetAndDeserializeValue<string>(missedKey2);

        Assert.Equal(missedValue1, missed1FromSecondary);
        Assert.Equal(missedValue2, missed2FromSecondary);
    }

    private static async Task DockerStop(string containerName)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("docker", $"stop {containerName}")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        process.Start();
        await process.WaitForExitAsync();
    }

    private static async Task DockerStart(string containerName)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("docker", $"start {containerName}")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        process.Start();
        await process.WaitForExitAsync();
    }
}
