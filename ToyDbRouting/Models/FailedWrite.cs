using ToyDbRouting.Clients;
using ToyDbRouting.Constants;

namespace ToyDbRouting.Models;

public record FailedWrite
{
    public required string Key { get; init; }
    public required ReplicaClient Replica { get; init; }
    public required string PartitionId { get; init; }
    public required WriteOperationType OperationType { get; init; }
    public required Func<ReplicaClient, int, Task> Operation { get; init; }
    public required DateTime FailedAt { get; init; }
    public int RetryCount { get; set; }
}
