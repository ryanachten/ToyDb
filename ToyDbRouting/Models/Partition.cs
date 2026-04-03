using System.Collections.Immutable;
using Grpc.Core;
using ToyDbRouting.Clients;
using static Grpc.Health.V1.HealthCheckResponse.Types;

namespace ToyDbRouting.Models;

public class Partition(PartitionConfiguration config)
{
    public string PartitionId => config.PartitionId;
    public ReplicaClient PrimaryReplica { get; private set; } = new(config.PrimaryReplicaAddress);
    public readonly ImmutableArray<ReplicaClient> SecondaryReplicas = config.SecondaryReplicaAddresses.Select(s => new ReplicaClient(s)).ToImmutableArray();

    public void UpdatePrimary(ReplicaClient newPrimary)
    {
        PrimaryReplica = newPrimary;
    }

    // TODO: not sure if this is the best algo for this. Should we be selecting random secondaries?
    // Probably not, we might need to check for data consistency etc if we're doing eventual consistency
    // Come back to it later
    public ReplicaClient GetReadReplica(IReadOnlyDictionary<string, ServingStatus> healthStates)
    {
        var healthySecondaries = GetHealthySecondaryReplicas(healthStates);
        if (healthySecondaries.Count > 0)
        {
            var rand = new Random();
            return healthySecondaries[rand.Next(healthySecondaries.Count)];
        }

        var healthyPrimary = GetHealthyPrimaryReplica(healthStates);
        if (healthyPrimary != null) return healthyPrimary;

        throw new RpcException(new Status(StatusCode.Unavailable, $"Partition {config.PartitionId} has no healthy replicas for reads"));
    }

    public List<ReplicaClient> GetHealthySecondaryReplicas(IReadOnlyDictionary<string, ServingStatus> healthStates)
    {
        return SecondaryReplicas
            .Where(r => healthStates.TryGetValue(r.Address, out var status) && status == ServingStatus.Serving).ToList();
    }

    private ReplicaClient? GetHealthyPrimaryReplica(IReadOnlyDictionary<string, ServingStatus> healthStates)
    {
        var primary = PrimaryReplica;
        var hasPrimaryHealth = healthStates.TryGetValue(primary.Address, out var health);

        if (!hasPrimaryHealth || health == ServingStatus.Serving) return primary;

        return null;
    }
}
