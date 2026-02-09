using Grpc.Core;
using ToyDbClient.Clients;
using ToyDbIntegrationTests.Helpers;

namespace ToyDbIntegrationTests.Tests;

public class ErrorHandlingTests
{
    private const string RoutingAddress = "https://localhost:8081";
    private readonly RoutingClient _client;

    public ErrorHandlingTests()
    {
        _client = new RoutingClient(RoutingAddress);
    }

    [Fact]
    public async Task GivenVeryLargeValue_WhenSetValue_ThenGrpcResourceExhaustionIsThrown() // TODO: this doesn't sound like good behaviour
    {
        var key = TestDataGenerator.CreateTestKey("large_value");
        var largeValue = TestDataGenerator.CreateRandomValue(5_000_000);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await _client.SetValue(key, largeValue);
        });

        Assert.Equal(StatusCode.ResourceExhausted, exception.StatusCode);
    }

    [Fact]
    public async Task GivenNullKey_WhenGetValue_ThenOperationHandledGracefully()
    {
        string? nullKey = null;

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _client.GetValue<string>(nullKey!);
        });
    }
}
