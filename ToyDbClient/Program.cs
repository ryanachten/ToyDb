using System.CommandLine;
using ToyDbClient.Services;

namespace ToyDbClient;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var commandService = new CommandService();
        var rootCommand = commandService.CreateRootCommand();
        return await rootCommand.InvokeAsync(args);
    }
}