using Microsoft.Extensions.Logging;
using System.IO.Hashing;
using System.Text;

namespace ToyDbClient.Clients;

public class DbPartitionClient(ILogger<DbPartitionClient> logger, List<string> partitionAddresses) : IDbClient
{
    private readonly DbClient[] _dbClients = partitionAddresses.Select(address => new DbClient(address)).ToArray();

    public Task DeleteValue(string key)
    {
        var client = GetPartitionClient(key);
        return client.DeleteValue(key);
    }

    public Task<T> GetValue<T>(string key)
    {
        var client = GetPartitionClient(key);
        return client.GetValue<T>(key);
    }

    public async Task<Dictionary<string, string>> PrintAllValues()
    {
        var allValues = new Dictionary<string, string>();
        
        foreach (var client in _dbClients) { 
            var values = await client.PrintAllValues();
            
            if (values == null) continue;

            foreach (var value in values)
            {
                allValues.Add(value.Key, value.Value);
            }
        }

        return allValues;
    }

    public Task<T> SetValue<T>(string key, T value)
    {
        var client = GetPartitionClient(key);
        return client.SetValue(key, value);
    }

    /// <summary>
    /// Returns client for database partition based on hash of key
    /// </summary>
    private DbClient GetPartitionClient(string key)
    {
        // Compute hash based on key value
        // We use xxHash here because it's a faster hashing solution than a cryptographic algorithm like SHA256
        var computedHash = XxHash32.Hash(Encoding.UTF8.GetBytes(key));

        // Convert hash to int and modulo to get consistent partition index
        var index = Math.Abs(BitConverter.ToInt32(computedHash) % _dbClients.Length);

        logger.LogInformation("Selected partition: {Partition} for key: {Key}", index, key);

        return _dbClients[index];
    }
}
