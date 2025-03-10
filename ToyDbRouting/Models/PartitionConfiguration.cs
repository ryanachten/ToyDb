namespace ToyDbRouting.Models;

public class PartitionConfiguration
{
    public required string PartitionId { get; set; }
    public required string PrimaryReplicaAddress { get; set; }
    public required string[] SecondaryReplicaAddresses { get; set; }
}
