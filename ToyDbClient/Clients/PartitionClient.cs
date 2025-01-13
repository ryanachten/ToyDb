using Microsoft.Extensions.Logging;
using System.IO.Hashing;
using System.Text;
using ToyDbClient.Extensions;
using ToyDbClient.Models;
using ToyDbClient.Services;

namespace ToyDbClient.Clients;

public class PartitionClient(ILogger<PartitionClient> logger, List<PartitionConfiguration> partitionConfigurations, int? completedSecondaryWritesThreshold)
{
    private readonly Partition[] _partitions = partitionConfigurations.Select(config => new Partition(config)).ToArray();
    private readonly MonotonicClock _monotonicClock = new();

    public Task<T> GetValue<T>(string key)
    {
        var partition = GetPartition(key);

        var replica = partition.GetReadReplica();
        return replica.GetValue<T>(key);
    }

    public async Task<Dictionary<string, string>> GetAllValues()
    {
        var allValues = new Dictionary<string, string>();

        foreach (var partition in _partitions)
        {
            var replica = partition.GetReadReplica();
            var values = await replica.GetAllValues();

            if (values == null) continue;

            foreach (var value in values)
            {
                allValues.Add(value.Key, value.Value);
            }
        }

        return allValues;
    }

    public async Task<T> SetValue<T>(string key, T value)
    {
        var timestamp = _monotonicClock.GetMonotonicNow();

        var partition = GetPartition(key);

        var primaryTask = partition.PrimaryReplica.SetValue(timestamp, key, value);
        
        var secondaryTasks = partition.SecondaryReplicas.Select(r => r.SetValue(timestamp, key, value));
        var secondaryThresholdTask = secondaryTasks.WhenThresholdCompleted(completedSecondaryWritesThreshold ?? partition.SecondaryReplicas.Length);

        // TODO: handle partital writes, node outages, etc
        await Task.WhenAll(primaryTask, secondaryThresholdTask);

        return value;
    }

    public async Task DeleteValue(string key)
    {
        var timestamp = _monotonicClock.GetMonotonicNow();

        var partition = GetPartition(key);

        var primaryTask = partition.PrimaryReplica.DeleteValue(timestamp, key);

        var secondaryTasks = partition.SecondaryReplicas.Select(r => r.DeleteValue(timestamp, key));
        var secondaryThresholdTask = secondaryTasks.WhenThresholdCompleted(completedSecondaryWritesThreshold ?? partition.SecondaryReplicas.Length);

        // TODO: handle partital writes, node outages, etc
        await Task.WhenAll(primaryTask, secondaryThresholdTask);
    }

    /// <summary>
    /// Returns database partition based on hash of key
    /// </summary>
    private Partition GetPartition(string key)
    {
        // Compute hash based on key value
        // We use xxHash here because it's a faster hashing solution than a cryptographic algorithm like SHA256
        var computedHash = XxHash32.Hash(Encoding.UTF8.GetBytes(key));

        // Convert hash to int and modulo to get consistent partition index
        var index = Math.Abs(BitConverter.ToInt32(computedHash) % _partitions.Length);

        logger.LogInformation("Selected partition: {Partition} for key: {Key}", index, key);

        return _partitions[index];
    }
}
