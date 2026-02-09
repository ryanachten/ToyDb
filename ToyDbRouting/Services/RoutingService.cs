using AutoMapper;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using System.IO.Hashing;
using System.Text;
using ToyDbRouting.Extensions;
using ToyDbRouting.Models;
using Data = ToyDbContracts.Data;
using Routing = ToyDbContracts.Routing;

namespace ToyDbRouting.Services;

public class RoutingService(
    IOptions<RoutingOptions> routingOptions,
    ILogger<RoutingService> logger,
    IMapper mapper,
    INtpService ntpService
) : Routing.Routing.RoutingBase
{
    private readonly Partition[] _partitions = routingOptions.Value.Partitions.Select(config => new Partition(config)).ToArray();
    private readonly int? completedSecondaryWritesThreshold = routingOptions.Value.CompletedSecondaryWritesThreshold;

    public override async Task<Routing.KeyValueResponse> GetValue(Routing.GetRequest request, ServerCallContext context)
    {
        var partition = GetPartition(request.Key);

        var replica = partition.GetReadReplica();
        var response = await replica.GetValue(request.Key);

        return mapper.Map<Routing.KeyValueResponse>(response);
    }

    public override async Task<Routing.GetAllValuesResponse> GetAllValues(Routing.GetAllValuesRequest request, ServerCallContext context)
    {
        var allValues = new Routing.GetAllValuesResponse();

        var partitionRequests = _partitions.Select(p => p.GetReadReplica().GetAllValues());

        await Task.WhenAll(partitionRequests);

        foreach (var partition in partitionRequests)
        {
            if (partition.Result.Values == null) continue;

            foreach (var value in partition.Result.Values)
            {
                if (value == null) continue;

                allValues.Values.Add(mapper.Map<Routing.KeyValueResponse>(value));
            }
        }

        return allValues;
    }

    public override async Task<Routing.KeyValueResponse> SetValue(Routing.KeyValueRequest request, ServerCallContext context)
    {
        var dbRequest = mapper.Map<Data.KeyValueRequest>(request);
        dbRequest.Timestamp = Timestamp.FromDateTime(ntpService.Now);

        var partition = GetPartition(request.Key);

        var primaryTask = partition.PrimaryReplica.SetValue(dbRequest);

        var secondaryTasks = partition.SecondaryReplicas.Select(r => r.SetValue(dbRequest));
        var secondaryThresholdTask = secondaryTasks.WhenThresholdCompleted(completedSecondaryWritesThreshold ?? partition.SecondaryReplicas.Length);

        // TODO: handle partital writes, node outages, etc
        await Task.WhenAll(primaryTask, secondaryThresholdTask);

        return mapper.Map<Routing.KeyValueResponse>(primaryTask.Result);
    }

    public override async Task<Routing.DeleteResponse> DeleteValue(Routing.DeleteRequest request, ServerCallContext context)
    {
        var timestamp = ntpService.Now;

        var partition = GetPartition(request.Key);

        var primaryTask = partition.PrimaryReplica.DeleteValue(timestamp, request.Key);

        var secondaryTasks = partition.SecondaryReplicas.Select(r => r.DeleteValue(timestamp, request.Key));
        var secondaryThresholdTask = secondaryTasks.WhenThresholdCompleted(completedSecondaryWritesThreshold ?? partition.SecondaryReplicas.Length);

        // TODO: handle partital writes, node outages, etc
        await Task.WhenAll(primaryTask, secondaryThresholdTask);

        return new Routing.DeleteResponse();
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
