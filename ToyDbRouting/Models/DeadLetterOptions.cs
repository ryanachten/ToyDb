namespace ToyDbRouting.Models;

public class DeadLetterOptions
{
    /// <summary>
    /// Maximum number of times a failed write will be retried by the DLQ before being discarded
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Interval (in seconds) between DLQ processing runs
    /// </summary>
    public int ProcessingIntervalSeconds { get; set; }

    /// <summary>
    /// Retry options for individual replay attempts within each processing run
    /// </summary>
    public RetryOptions RetryOptions { get; set; } = new();
}
