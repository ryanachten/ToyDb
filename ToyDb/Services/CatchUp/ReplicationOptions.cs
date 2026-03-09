namespace ToyDb.Services.CatchUp;

public class ReplicationOptions
{
    public const string Key = "Replication";

    /// <summary>
    /// Temporary: address of this node's primary replica, used for startup catch-up.
    /// Will be superseded once nodes have full cluster self-awareness (REPLICATION_REVIEW.md § 7, Phase 3, Point 7).
    /// If null or empty, this node is treated as a primary and catch-up is skipped.
    /// </summary>
    public string? PrimaryNodeAddress { get; set; }

    /// <summary>
    /// Initial delay before the first reconnect attempt in milliseconds.
    /// Doubles on each failure up to <see cref="RetryMaxDelayMs"/>.
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay between reconnect attempts in milliseconds.
    /// </summary>
    public int RetryMaxDelayMs { get; set; } = 30000;
}
