namespace ToyDbRouting.Models;

public class RoutingOptions
{
    public const string Key = "routing";

    /// <summary>
    /// Minimum number of writes to be completed before a request will complete
    /// If the threshold is equal to the number of partitions, this will result in higher latency and higher consistency
    /// The lower the threshold, the lower the latency and consistency
    /// </summary>
    public int? CompletedSecondaryWritesThreshold { get; set; }

    /// <summary>
    /// Partitions comprising the database network
    /// </summary>
    public required List<PartitionConfiguration> Partitions { get; set; }

    /// <summary>
    /// Primary replica retry configuration
    /// Note: Retrying primary writes increases latency but improves write reliability
    /// </summary>
    public RetryOptions PrimaryRetryOptions { get; set; } = new()
    {
        MaxRetries = 0,
        BaseDelayMs = 100,
        MaxDelayMs = 5000
    };

    /// <summary>
    /// Secondary replica retry configuration
    /// Note: Retrying secondary writes increases latency but improves replica consistency
    /// </summary>
    public RetryOptions SecondaryRetryOptions { get; set; } = new()
    {
        MaxRetries = 3,
        BaseDelayMs = 100,
        MaxDelayMs = 5000
    };
}

public class RetryOptions
{
    /// <summary>
    /// Maximum number of retry attempts (0 = no retries)
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Initial delay before first retry in milliseconds
    /// </summary>
    public int BaseDelayMs { get; set; }

    /// <summary>
    /// Maximum delay between retries in milliseconds (caps exponential backoff)
    /// </summary>
    public int MaxDelayMs { get; set; }
}