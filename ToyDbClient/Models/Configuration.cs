using System.Text.Json;

namespace ToyDbClient.Models;

internal class Configuration
{
    public required List<string> Partitions { get; set; }

    public static Configuration? Load(string filePath)
    {
        var contents = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<Configuration>(contents, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
    }
}
