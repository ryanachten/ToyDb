using System.CommandLine;
using ToyDbClient;

internal class Program
{
    private static readonly Option<string> _dbAddressOption = new (
        "--address",
        description: "Address the ToyDb instance is running on",
        getDefaultValue: () => "https://localhost:7274"
    );

    private static async Task<int> Main(string[] args)
    {
        var getCommand = CreateGetCommand();
        var setCommand = CreateSetCommand();
        var listCommand = CreateListCommand();

        var rootCommand = new RootCommand("Commandline client for ToyDb")
        {
            getCommand,
            setCommand,
            listCommand
        };
        
        rootCommand.AddGlobalOption(_dbAddressOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static Command CreateGetCommand()
    {
        var keyArgument = new Argument<string>("key", "The key to retrieve");
        var getCommand = new Command("get", "Retrieve the value of a key")
        {
            keyArgument
        };

        getCommand.SetHandler(async (key, dbAddress) =>
        {
            var client = new DbClient(dbAddress);
            var value = await client.GetValue(key);
            Console.WriteLine(value);
        }, keyArgument, _dbAddressOption);

        return getCommand;
    }

    private static Command CreateSetCommand() {
        var keyValuePairArgument = new Argument<string>("keyValue", "The key-value pair in the format key=value");
        var setCommand = new Command("set", "Set a key-value pair")
        {
            keyValuePairArgument
        };

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
        }, keyValuePairArgument, _dbAddressOption);

        return setCommand;
    }

    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "List all key value pairs");

        listCommand.SetHandler(async (dbAddress) =>
        {
            var client = new DbClient(dbAddress);
            var values = await client.GetAllValues();
            foreach (var value in values)
            {
                Console.WriteLine($"{value.Key}: {value.Value}");
            }
        }, _dbAddressOption);

        return listCommand;
    }
}