using System.CommandLine;
using ToyDbClient.Services;

namespace ToyDbClient;

internal static class Program
{
    private static readonly CommandService _commandService = new();

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = _commandService.CreateRootCommand();
        return await rootCommand.InvokeAsync(args);
    }
}