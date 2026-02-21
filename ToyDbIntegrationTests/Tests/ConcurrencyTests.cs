using ToyDbClient.Clients;
using ToyDbIntegrationTests.Helpers;

namespace ToyDbIntegrationTests.Tests;

public class ConcurrencyTests
{
    private const string RoutingAddress = "https://localhost:8081";
    private readonly RoutingClient _client;

    public ConcurrencyTests()
    {
        _client = new RoutingClient(RoutingAddress, IntegrationTestConfig.SkipCertificateValidation);
    }

    [Fact]
    public async Task GivenMultipleClients_WhenWritingDifferentKeysSimultaneously_ThenAllWritesSucceed()
    {
        var keys = TestDataGenerator.CreateTestKeys(10, "concurrent_write");
        var expectedValues = new Dictionary<string, string>();

        var writeTasks = keys.Select(async key =>
        {
            var value = TestDataGenerator.CreateRandomValue();
            expectedValues[key] = value;
            await _client.SetValue(key, value);
        }).ToList();

        await Task.WhenAll(writeTasks);

        foreach (var (key, expectedValue) in expectedValues)
        {
            var actualValue = await _client.GetValue<string>(key);
            Assert.Equal(expectedValue, actualValue);
        }
    }

    [Fact]
    public async Task GivenMultipleClients_WhenWritingSameKeySimultaneously_ThenLastWriteWins()
    {
        var key = TestDataGenerator.CreateTestKey("concurrent_same_key");
        var possibleValues = new List<string>();

        var writeTasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var value = $"value_{i}_{TestDataGenerator.CreateRandomValue(10)}";
            possibleValues.Add(value);
            await _client.SetValue(key, value);
        }).ToList();

        await Task.WhenAll(writeTasks);

        var finalValue = await _client.GetValue<string>(key);

        Assert.Contains(finalValue, possibleValues);
    }

    [Fact]
    public async Task GivenMultipleClients_WhenMixedOperationsOnDifferentKeys_ThenNoDataLoss()
    {
        var allKeys = TestDataGenerator.CreateTestKeys(20, "mixed_ops");
        var writtenValues = new Dictionary<string, string>();
        var keysToDelete = allKeys.Take(5).ToList();
        var keysToWrite = allKeys.Skip(5).Take(15).ToList();
        var keysToRead = allKeys.Take(10).ToList();

        foreach (var key in keysToRead)
        {
            var initialValue = TestDataGenerator.CreateRandomValue();
            await _client.SetValue(key, initialValue);
        }

        var tasks = new List<Task>();

        foreach (var key in keysToWrite)
        {
            var value = TestDataGenerator.CreateRandomValue();
            writtenValues[key] = value;
            tasks.Add(_client.SetValue(key, value));
        }

        foreach (var key in keysToRead)
        {
            tasks.Add(_client.GetValue<string>(key));
        }

        foreach (var key in keysToDelete)
        {
            tasks.Add(_client.DeleteValue(key));
        }

        await Task.WhenAll(tasks);

        foreach (var (key, expectedValue) in writtenValues)
        {
            var actualValue = await _client.GetValue<string>(key);
            Assert.Equal(expectedValue, actualValue);
        }

        foreach (var key in keysToDelete)
        {
            var value = await _client.GetValue<string>(key);
            Assert.Null(value);
        }
    }
}
