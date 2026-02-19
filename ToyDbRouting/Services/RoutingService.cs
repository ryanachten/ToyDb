using AutoMapper;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using System.IO.Hashing;
using System.Text;
using ToyDbContracts.Data;
using ToyDbRouting.Clients;
using ToyDbRouting.Extensions;
using ToyDbRouting.Models;
using Data = ToyDbContracts.Data;
using Routing = ToyDbContracts.Routing;

namespace ToyDbRouting.Services;

public class RoutingService(
    IOptions<RoutingOptions> routingOptions,
    ILogger<RoutingService> logger,
    IMapper mapper,
    INtpService ntpService,
    HealthProbeService healthProbeService
) : Routing.Routing.RoutingBase
{
    private readonly Partition[] _partitions = routingOptions.Value.Partitions.Select(config => new Partition(config)).ToArray();
    private readonly int? completedSecondaryWritesThreshold = routingOptions.Value.CompletedSecondaryWritesThreshold;

    internal enum OperationType
    {
        Write,
        Delete
    }

    internal record ReplicaExecutionResult<TPrimaryResponse>
    {
        public required TPrimaryResponse PrimaryResponse { get; init; }
        public required int ReplicasCompleted { get; init; }
        public required int ReplicasTotal { get; init; }
        public required List<string> Warnings { get; init; }
    }

    public override async Task<Routing.KeyValueResponse> GetValue(Routing.GetRequest request, ServerCallContext context)
    {
        var partition = GetPartition(request.Key);

        var replica = partition.GetReadReplica(healthProbeService.HealthStates);
        var response = await replica.GetValue(request.Key);

        return mapper.Map<Routing.KeyValueResponse>(response);
    }

    public override async Task<Routing.GetAllValuesResponse> GetAllValues(Routing.GetAllValuesRequest request, ServerCallContext context)
    {
        var allValues = new Routing.GetAllValuesResponse();

        var partitionRequests = _partitions.Select(partition =>
        {
            var replica = partition.GetReadReplica(healthProbeService.HealthStates);
            return replica?.GetAllValues() ?? Task.FromResult(new GetAllValuesResponse());
        });

        await Task.WhenAll(partitionRequests);

        foreach (var partition in partitionRequests)
        {
            if (partition.Result == null || partition.Result.Values == null) continue;

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

        var result = await ExecuteWithReplicaThresholdAsync(
            () => partition.PrimaryReplica.SetValue(dbRequest),
            (replica, index) => replica.SetValue(dbRequest),
            partition,
            request.Key,
            OperationType.Write);

        var response = mapper.Map<Routing.KeyValueResponse>(result.PrimaryResponse);
        response.ReplicasWritten = result.ReplicasCompleted;
        response.ReplicasTotal = result.ReplicasTotal;
        response.Warnings.AddRange(result.Warnings);

        return response;
    }

    public override async Task<Routing.DeleteResponse> DeleteValue(Routing.DeleteRequest request, ServerCallContext context)
    {
        var timestamp = ntpService.Now;

        var partition = GetPartition(request.Key);

        var result = await ExecuteWithReplicaThresholdAsync(
            async () =>
            {
                await partition.PrimaryReplica.DeleteValue(timestamp, request.Key);
                return 0;
            },
            (replica, index) => replica.DeleteValue(timestamp, request.Key),
            partition,
            request.Key,
            OperationType.Delete);

        return new Routing.DeleteResponse
        {
            Warnings = { result.Warnings }
        };
    }

    internal async Task<ReplicaExecutionResult<TPrimaryResponse>> ExecuteWithReplicaThresholdAsync<TPrimaryResponse>(
        Func<Task<TPrimaryResponse>> primaryOperation,
        Func<ReplicaClient, int, Task> secondaryOperation,
        Partition partition,
        string key,
        OperationType operationType)
    {
        var operationName = operationType == OperationType.Write ? "write" : "delete";
        var operationNamePast = operationType == OperationType.Write ? "wrote" : "deleted";

        // TODO: implment catch up the the case of failure to avoid lost writes and missing reads from secondaries
        var healthySecondaries = partition.GetHealthySecondaryReplicas(healthProbeService.HealthStates);

        int replicasCompleted = 0;
        int replicasTotal = 1 + healthySecondaries.Count;
        var warnings = new List<string>();

        TPrimaryResponse primaryResponse;
        try
        {
            primaryResponse = await RetryHelper.ExecuteWithRetryAsync(
                primaryOperation,
                routingOptions.Value.PrimaryRetryOptions,
                logger,
                $"Primary {operationName} for key {key}");
            replicasCompleted++;
        }
        catch (Exception ex)
        {
            logger.LogError("Primary replica {Operation} failed for key {Key}: {Error}",
                operationName, key, ex.Message);
            throw;
        }

        var threshold = completedSecondaryWritesThreshold ?? healthySecondaries.Count;
        var successfulSecondaries = 0;
        var secondaryTasksCompleted = 0;

        var secondaryTasks = healthySecondaries.Select(async (replica, index) =>
        {
            try
            {
                await RetryHelper.ExecuteWithRetryAsync(
                    () => secondaryOperation(replica, index),
                    routingOptions.Value.SecondaryRetryOptions,
                    logger,
                    $"Secondary[{index}] {operationName} for key {key}");
                return (Index: index, Success: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Secondary replica[{Index}] {Operation} failed for key {Key}: {Error}",
                    index, operationName, key, ex.Message);
                return (Index: index, Success: false);
            }
        }).ToList();

        await foreach (var result in Task.WhenEach(secondaryTasks))
        {
            var (index, success) = await result;
            secondaryTasksCompleted++;

            if (success)
            {
                successfulSecondaries++;
                replicasCompleted++;
            }

            if (successfulSecondaries >= threshold)
            {
                break;
            }
        }

        if (successfulSecondaries < threshold)
        {
            throw new Exception($"Failed to meet secondary {operationName} threshold. Required: {threshold}, Succeeded: {successfulSecondaries}");
        }

        var remainingTasks = secondaryTasks.Skip(secondaryTasksCompleted).ToList();
        if (remainingTasks.Count != 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.WhenAll(remainingTasks);
            });
        }

        if (replicasCompleted < replicasTotal)
        {
            var failedCount = replicasTotal - replicasCompleted;
            var message = $"Partial success: {operationNamePast} to {replicasCompleted} of {replicasTotal} replicas ({failedCount} replica(s) failed)";
            warnings.Add(message);
            logger.LogWarning("{Message} for key {Key}", message, key);
        }

        return new ReplicaExecutionResult<TPrimaryResponse>
        {
            PrimaryResponse = primaryResponse,
            ReplicasCompleted = replicasCompleted,
            ReplicasTotal = replicasTotal,
            Warnings = warnings
        };
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
