using System.Collections.Immutable;
using ToyDbClient.Clients;

namespace ToyDbClient.Models;

public class Partition(PartitionConfiguration config)
{
    public readonly ReplicaClient PrimaryReplica = new(config.PrimaryReplicaAddress);
    public readonly ImmutableArray<ReplicaClient> SecondaryReplicas = config.SecondaryReplicaAddresses.Select(s => new ReplicaClient(s)).ToImmutableArray();

    private readonly Random _rand = new();

    // TODO: should we be selecting random secondaries?
    // Probably not, we might need to check for data consistency etc if we're doing eventual consistency
    // Come back to it later
    public ReplicaClient GetReadReplica()
    {
        var index = _rand.Next(0, SecondaryReplicas.Length);
        return SecondaryReplicas[index];
    }
}
