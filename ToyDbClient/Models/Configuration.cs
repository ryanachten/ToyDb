using System.Text.Json;

namespace ToyDbClient.Models;

internal class Configuration
{
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

    public static Configuration? Load(string filePath)
    {
        var contents = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<Configuration>(contents, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
    }
}
