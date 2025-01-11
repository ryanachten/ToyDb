using Microsoft.Extensions.Logging;
using System.CommandLine;
using ToyDbClient.Clients;
using ToyDbClient.Models;

namespace ToyDbClient.Services;

public class CommandService
{
    private readonly Option<string> _configOption = new(
        "config",
        description: "Path to ToyDb configuration",
        getDefaultValue: () => "C:\\dev\\ToyDb\\ToyDbClient\\toydb.json"); // TODO: make this a relative path or something

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
            _keyArgument,
            _configOption
        };

        deleteCommand.SetHandler(async (key, configPath) =>
        {
            var client = CreateDbClient(configPath);
            await client.DeleteValue(key);
        }, _keyArgument, _configOption);

        return deleteCommand;
    }

    private Command CreateGetCommand()
    {
        var getCommand = new Command("get", "Retrieve the value of a key")
        {
            _keyArgument,
            _configOption,
        };

        getCommand.SetHandler(async (key, configPath) =>
        {
            var client = CreateDbClient(configPath);
            var value = await client.GetValue<string>(key);
            Console.WriteLine(value);
        }, _keyArgument, _configOption);

        return getCommand;
    }

    private Command CreateSetCommand()
    {
        // TODO: it would be nice to be able to set data type as part of this CLI
        var keyValuePairArgument = new Argument<string>("keyValue", "The key-value pair in the format key=value");
        var setCommand = new Command("set", "Set a key-value pair")
        {
            keyValuePairArgument,
            _configOption
        };

        setCommand.SetHandler(async (kvp, configPath) =>
        {
            var parts = kvp.Split('=', 2);
            if (parts.Length != 2)
            {
                Console.WriteLine("ERROR: Please use the format key=value");
                return;
            }

            var key = parts[0];
            var value = parts[1];

            var client = CreateDbClient(configPath);
            var updatedValue = await client.SetValue(key, value);
            Console.WriteLine(updatedValue);
        }, keyValuePairArgument, _configOption);

        return setCommand;
    }

    private Command CreateListCommand()
    {
        var listCommand = new Command("list", "List all key value pairs")
        {
            _configOption
        };

        listCommand.SetHandler(async (configPath) =>
        {
            var client = CreateDbClient(configPath);
            var values = await client.GetAllValues();
            foreach (var value in values)
            {
                Console.WriteLine($"{value.Key}: {value.Value}");
            }
        }, _configOption);

        return listCommand;
    }

    private static PartitionClient CreateDbClient(string configPath)
    {
        var config = Configuration.Load(configPath);
        
        if (config == null) throw new CommandLineConfigurationException($"Configuration at {configPath} not found");

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<PartitionClient>();

        return new PartitionClient(logger, config.Partitions, config.CompletedSecondaryWritesThreshold);
    }
}
