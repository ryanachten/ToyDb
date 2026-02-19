using System.Collections.Immutable;
using Grpc.Core;
using ToyDbRouting.Clients;
using static Grpc.Health.V1.HealthCheckResponse.Types;

namespace ToyDbRouting.Models;

public class Partition(PartitionConfiguration config)
{
    public readonly ReplicaClient PrimaryReplica = new(config.PrimaryReplicaAddress);
    public readonly ImmutableArray<ReplicaClient> SecondaryReplicas = config.SecondaryReplicaAddresses.Select(s => new ReplicaClient(s)).ToImmutableArray();

    // TODO: not sure if this is the best algo for this. Should we be selecting random secondaries?
    // Probably not, we might need to check for data consistency etc if we're doing eventual consistency
    // Come back to it later
    public ReplicaClient GetReadReplica(IReadOnlyDictionary<string, ServingStatus> healthStates)
    {
        var healthySecondary = GetHealthySecondaryReplica(healthStates);
        if (healthySecondary != null) return healthySecondary;

        var healthyPrimary = GetHealthyPrimaryReplica(healthStates);
        if (healthyPrimary != null) return healthyPrimary;

        throw new RpcException(new Status(StatusCode.Unavailable, $"Partition {config.PartitionId} has no healthy replcas for reads"));
    }

    private ReplicaClient? GetHealthyPrimaryReplica(IReadOnlyDictionary<string, ServingStatus> healthStates)
    {
        var hasPrimaryHealth = healthStates.TryGetValue(PrimaryReplica.Address, out var health);

        if (!hasPrimaryHealth || health != ServingStatus.Serving) return null;

        return PrimaryReplica;
    }

    private ReplicaClient? GetHealthySecondaryReplica(IReadOnlyDictionary<string, ServingStatus> healthStates)
    {
        var healthySecondaries = SecondaryReplicas
            .Where(r => healthStates.TryGetValue(r.Address, out var status) && status == ServingStatus.Serving)
            .ToList();

        if (healthySecondaries.Count == 0) return null;

        var rand = new Random();

        return healthySecondaries[rand.Next(healthySecondaries.Count)];
    }
}
