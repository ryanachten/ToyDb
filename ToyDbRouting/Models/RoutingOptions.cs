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
}