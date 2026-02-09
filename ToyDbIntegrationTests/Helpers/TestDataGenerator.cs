namespace ToyDbIntegrationTests.Helpers;

/// <summary>
/// Generates test data with unique keys to avoid collisions between tests.
/// </summary>
public static class TestDataGenerator
{
    /// <summary>
    /// Creates a unique key for a test with an optional suffix.
    /// Format: test_{guid}_{suffix}
    /// </summary>
    public static string CreateTestKey(string? suffix = null)
    {
        var guid = Guid.NewGuid().ToString("N")[..8]; // Use first 8 chars for brevity
        return suffix != null ? $"test_{guid}_{suffix}" : $"test_{guid}";
    }

    /// <summary>
    /// Creates multiple unique test keys with sequential suffixes.
    /// </summary>
    public static List<string> CreateTestKeys(int count, string? prefix = null)
    {
        var baseGuid = Guid.NewGuid().ToString("N")[..8];
        var keys = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var key = prefix != null
                ? $"test_{baseGuid}_{prefix}_{i}"
                : $"test_{baseGuid}_{i}";
            keys.Add(key);
        }

        return keys;
    }

    /// <summary>
    /// Generates a random string value of specified length.
    /// </summary>
    public static string CreateRandomValue(int length = 20)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// Creates a key-value pair with unique test data.
    /// </summary>
    public static (string key, string value) CreateTestKeyValue(string? suffix = null)
    {
        var key = CreateTestKey(suffix);
        var value = CreateRandomValue();
        return (key, value);
    }
}
