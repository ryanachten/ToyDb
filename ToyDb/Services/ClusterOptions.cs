namespace ToyDb.Services;

public class ClusterOptions
{
    public const string Key = "Cluster";

    public required string NodeId { get; set; }

    public required string PartitionId { get; set; }

    public required List<string> PeerAddresses { get; set; }

    public string? SelfAddress { get; set; }

    public int ElectionTimeoutMs { get; set; }

    public int HeartbeatIntervalMs { get; set; }
}
