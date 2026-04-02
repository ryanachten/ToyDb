namespace ToyDb.Services;

public enum ReplicaRole
{
    Primary,
    Secondary
}

public class ReplicaOptions
{
    public static readonly string Key = "Replica";

    public required ReplicaRole Role { get; set; }

    public string? PrimaryAddress { get; set; }

    public string? NodeId { get; set; }

    public string? PartitionId { get; set; }
}
