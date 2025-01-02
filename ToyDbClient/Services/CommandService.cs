using System.CommandLine;
using ToyDbClient.Clients;

namespace ToyDbClient.Services;

public class CommandService
{
    private readonly DbPartitionClient _dbClient = new();

    private readonly Argument<string> _keyArgument = new("key", "The key to retrieve");

    public Command CreateRootCommand()
    {
        var getCommand = CreateGetCommand();
        var setCommand = CreateSetCommand();
        var deleteCommand = CreateDeleteCommand();
        var listCommand = CreateListCommand();

        var rootCommand = new RootCommand("Commandline client for ToyDb")
        {
            getCommand,
            setCommand,
            deleteCommand,
            listCommand
        };

        return rootCommand;
    }

    private Command CreateDeleteCommand()
    {
        var deleteCommand = new Command("delete", "Delete the value of a key")
        {
            _keyArgument
        };

        deleteCommand.SetHandler(async (key) =>
        {
            await _dbClient.DeleteValue(key);
        }, _keyArgument);

        return deleteCommand;
    }

    private Command CreateGetCommand()
    {
        var getCommand = new Command("get", "Retrieve the value of a key")
        {
            _keyArgument
        };

        getCommand.SetHandler(async (key) =>
        {
            var value = await _dbClient.GetValue<string>(key);
            Console.WriteLine(value);
        }, _keyArgument);

        return getCommand;
    }

    private Command CreateSetCommand()
    {
        // TODO: it would be nice to be able to set data type as part of this CLI
        var keyValuePairArgument = new Argument<string>("keyValue", "The key-value pair in the format key=value");
        var setCommand = new Command("set", "Set a key-value pair")
        {
            keyValuePairArgument
        };

        setCommand.SetHandler(async (kvp) =>
        {
            var parts = kvp.Split('=', 2);
            if (parts.Length != 2)
            {
                Console.WriteLine("ERROR: Please use the format key=value");
                return;
            }

            var key = parts[0];
            var value = parts[1];

            var updatedValue = await _dbClient.SetValue(key, value);
            Console.WriteLine(updatedValue);
        }, keyValuePairArgument);

        return setCommand;
    }

    private Command CreateListCommand()
    {
        var listCommand = new Command("list", "List all key value pairs");

        listCommand.SetHandler(async () =>
        {
            var values = await _dbClient.PrintAllValues();
            foreach (var value in values)
            {
                Console.WriteLine($"{value.Key}: {value.Value}");
            }
        });

        return listCommand;
    }
}
