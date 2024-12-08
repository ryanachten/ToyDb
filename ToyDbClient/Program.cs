using System.CommandLine;
using ToyDbClient;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var keyArgument = new Argument<string>("key", "The key to retrieve");
        var getCommand = new Command("get", "Retrieve the value of a key")
        {
            keyArgument
        };

        var keyValuePairArgument = new Argument<string>("keyValue", "The key-value pair in the format key=value");
        var setCommand = new Command("set", "Set a key-value pair")
        {
            keyValuePairArgument
        };

        var rootCommand = new RootCommand("Commandline client for ToyDb")
        {
            getCommand,
            setCommand
        };

        var dbAddressOption = new Option<string>(
            "--address",
            description: "Address the ToyDb instance is running on",
            getDefaultValue: () => "https://localhost:7274"
        );
        rootCommand.AddGlobalOption(dbAddressOption);

        getCommand.SetHandler(async (key, dbAddress) =>
        {
            var client = new DbClient(dbAddress);
            var value = await client.GetValue(key);
            Console.WriteLine(value);
        }, keyArgument, dbAddressOption);

        setCommand.SetHandler(async (kvp, dbAddress) =>
        {
            var parts = kvp.Split('=', 2);
            if (parts.Length != 2)
            {
                Console.WriteLine("ERROR: Please use the format key=value");
                return;
            }

            var key = parts[0];
            var value = parts[1];

            var client = new DbClient(dbAddress);
            var updatedValue = await client.SetValue(key, value);
            Console.WriteLine(updatedValue);
        }, keyValuePairArgument, dbAddressOption);

        return await rootCommand.InvokeAsync(args);
    }
}