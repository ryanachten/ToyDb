namespace ToyDbClient.Clients;

public class DbPartitionClient(List<string> partitionAddresses) : IDbClient
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
        var hash = key.GetHashCode();
        var index = Math.Abs(hash % _dbClients.Length);
        
        return _dbClients[index];
    }
}
