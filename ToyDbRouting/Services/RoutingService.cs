using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using System.IO.Hashing;
using System.Text;
using ToyDbContracts.Data;
using ToyDbRouting.Extensions;
using ToyDbRouting.Models;

namespace ToyDbRouting.Services;

public class RoutingService(
    IOptions<RoutingOptions> routingOptions,
    ILogger<RoutingService> logger
) : Data.DataBase
{
    private readonly Partition[] _partitions = routingOptions.Value.Partitions.Select(config => new Partition(config)).ToArray();
    private readonly NtpClock _ntpClock = new();
    private readonly int? completedSecondaryWritesThreshold = routingOptions.Value.CompletedSecondaryWritesThreshold;

    public override Task<KeyValueResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var partition = GetPartition(request.Key);

        var replica = partition.GetReadReplica();
        return replica.GetValue(request.Key);
    }

    public override async Task<GetAllValuesReresponse> GetAllValues(GetAllValuesRequest request, ServerCallContext context)
    {
        var allValues = new GetAllValuesReresponse();

        foreach (var partition in _partitions)
        {
            var replica = partition.GetReadReplica();
            var response = await replica.GetAllValues();

            if (response == null) continue;

            foreach (var value in response.Values)
            {
                if (value == null) continue;

                allValues.Values.Add(value);
            }

        }

        return allValues;
    }

    public override async Task<KeyValueResponse> SetValue(KeyValueRequest request, ServerCallContext context)
    {
        // TODO: ideally we would have different GRCP contracts for client vs routing to avoid this?
        request.Timestamp = Timestamp.FromDateTime(_ntpClock.Now);

        var partition = GetPartition(request.Key);

        var primaryTask = partition.PrimaryReplica.SetValue(request);

        var secondaryTasks = partition.SecondaryReplicas.Select(r => r.SetValue(request));
        var secondaryThresholdTask = secondaryTasks.WhenThresholdCompleted(completedSecondaryWritesThreshold ?? partition.SecondaryReplicas.Length);

        // TODO: handle partital writes, node outages, etc
        await Task.WhenAll(primaryTask, secondaryThresholdTask);

        return primaryTask.Result;
    }

    public override async Task<DeleteResponse> DeleteValue(DeleteRequest request, ServerCallContext context)
    {
        var timestamp = _ntpClock.Now;

        var partition = GetPartition(request.Key);

        var primaryTask = partition.PrimaryReplica.DeleteValue(timestamp, request.Key);

        var secondaryTasks = partition.SecondaryReplicas.Select(r => r.DeleteValue(timestamp, request.Key));
        var secondaryThresholdTask = secondaryTasks.WhenThresholdCompleted(completedSecondaryWritesThreshold ?? partition.SecondaryReplicas.Length);

        // TODO: handle partital writes, node outages, etc
        await Task.WhenAll(primaryTask, secondaryThresholdTask);

        return new DeleteResponse();
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
