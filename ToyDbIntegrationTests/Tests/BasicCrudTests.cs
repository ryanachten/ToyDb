using ToyDbClient.Clients;
using ToyDbIntegrationTests.Helpers;

namespace ToyDbIntegrationTests.Tests;

/// <summary>
/// Integration tests for basic CRUD operations through the full stack:
/// Client -> Routing Layer -> Database Nodes
/// </summary>
public class BasicCrudTests
{
    private const string RoutingAddress = "https://localhost:8081";
    private readonly RoutingClient _client;

    public BasicCrudTests()
    {
        _client = new RoutingClient(RoutingAddress, IntegrationTestConfig.SkipCertificateValidation);
    }

    [Fact]
    public async Task GivenNewKeyValue_WhenSetValue_ThenValueIsStoredAndReturned()
    {
        var (key, expectedValue) = TestDataGenerator.CreateTestKeyValue("set_test");

        var actualValue = await _client.SetValue(key, expectedValue);

        Assert.Equal(expectedValue, actualValue);
    }

    [Fact]
    public async Task GivenKeyExists_WhenGetValue_ThenCorrectValueIsReturned()
    {
        var (key, expectedValue) = TestDataGenerator.CreateTestKeyValue("get_test");
        await _client.SetValue(key, expectedValue);

        var actualValue = await _client.GetValue<string>(key);

        Assert.Equal(expectedValue, actualValue);
    }

    [Fact]
    public async Task GivenKeyDoesNotExist_WhenGetValue_ThenNullIsReturned()
    {
        var nonExistentKey = TestDataGenerator.CreateTestKey("nonexistent");

        var value = await _client.GetValue<string>(nonExistentKey);

        Assert.Null(value);
    }

    [Fact]
    public async Task GivenKeyExists_WhenDeleteValue_ThenKeyIsRemoved()
    {
        var (key, value) = TestDataGenerator.CreateTestKeyValue("delete_test");
        await _client.SetValue(key, value);

        var valueBeforeDelete = await _client.GetValue<string>(key);
        Assert.Equal(value, valueBeforeDelete);

        await _client.DeleteValue(key);

        var valueAfterDelete = await _client.GetValue<string>(key);
        Assert.Null(valueAfterDelete);
    }

    [Fact]
    public async Task GivenKeyDoesNotExist_WhenDeleteValue_ThenNoExceptionIsThrown()
    {
        var nonExistentKey = TestDataGenerator.CreateTestKey("delete_nonexistent");

        await _client.DeleteValue(nonExistentKey);
    }

    [Fact]
    public async Task GivenKeyExists_WhenSetValueWithNewData_ThenValueIsOverwritten()
    {
        var key = TestDataGenerator.CreateTestKey("update_test");
        var originalValue = "original_value";
        var updatedValue = "updated_value";

        await _client.SetValue(key, originalValue);
        var initialRetrieved = await _client.GetValue<string>(key);
        Assert.Equal(originalValue, initialRetrieved);

        await _client.SetValue(key, updatedValue);

        var finalRetrieved = await _client.GetValue<string>(key);
        Assert.Equal(updatedValue, finalRetrieved);
    }

    [Fact]
    public async Task GivenMultipleKeysExist_WhenGetAllValues_ThenAllKeysAreReturned()
    {
        var testKeys = TestDataGenerator.CreateTestKeys(5, "list_test");
        var testData = new Dictionary<string, string>();

        foreach (var key in testKeys)
        {
            var value = TestDataGenerator.CreateRandomValue();
            testData[key] = value;
            await _client.SetValue(key, value);
        }

        var allValues = await _client.GetAllValues();

        foreach (var (key, expectedValue) in testData)
        {
            Assert.Contains(allValues, kvp => kvp.Key == key && kvp.Value == expectedValue);
        }
    }

    [Theory]
    [MemberData(nameof(GetSpecialValueTestCases))]
    public async Task GivenSpecialValue_WhenSetValue_ThenValueIsStoredCorrectly(string testName, string value)
    {
        var key = TestDataGenerator.CreateTestKey(testName);

        await _client.SetValue(key, value);

        var retrievedValue = await _client.GetValue<string>(key);
        Assert.Equal(value, retrievedValue);
    }

    public static IEnumerable<object[]> GetSpecialValueTestCases()
    {
        yield return new object[] { "empty_value", string.Empty };
        yield return new object[] { "large_value", TestDataGenerator.CreateRandomValue(10000) };
        yield return new object[] { "special_chars", "Hello, 世界! 🌍 @#$%^&*()" };
    }

    [Fact]
    public async Task GivenSequentialOperations_WhenCrudOperationsExecuted_ThenConsistencyIsMaintained()
    {
        var key = TestDataGenerator.CreateTestKey("sequential");
        var values = new[] { "value1", "value2", "value3" };

        foreach (var value in values)
        {
            await _client.SetValue(key, value);
            var retrieved = await _client.GetValue<string>(key);
            Assert.Equal(value, retrieved);
        }

        await _client.DeleteValue(key);
        var deletedValue = await _client.GetValue<string>(key);
        Assert.Null(deletedValue);
    }
}
