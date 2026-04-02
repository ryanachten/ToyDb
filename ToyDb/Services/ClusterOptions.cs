namespace ToyDb.Services;

public class ClusterOptions
{
    public static readonly string Key = "Cluster";

    public required string NodeId { get; set; }

    public required string PartitionId { get; set; }

    public required List<string> PeerAddresses { get; set; }

    public int ElectionTimeoutMs { get; set; } = 5000;

    public int HeartbeatIntervalMs { get; set; } = 1500;
}
