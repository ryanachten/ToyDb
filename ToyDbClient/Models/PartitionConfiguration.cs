namespace ToyDbClient.Models;

public class PartitionConfiguration
{
    public required string PrimaryReplicaAddress {  get; set; }
    public required string[] SecondaryReplicaAddresses { get; set; }
}
