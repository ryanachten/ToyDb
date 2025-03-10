using Microsoft.Extensions.Logging;
using System.CommandLine;
using ToyDbClient.Clients;
using ToyDbClient.Models;

namespace ToyDbClient.Services;

public class CommandService
{
    private readonly Option<string> _routingAddress = new(
        "routingAddress",
        description: "Address for ToyDB routing service",
        getDefaultValue: () => "https://localhost:8081");

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
            _routingAddress
        };

        deleteCommand.SetHandler(async (key, routingAddress) =>
        {
            var client = CreateRoutingClient(routingAddress);
            await client.DeleteValue(key);
        }, _keyArgument, _routingAddress);

        return deleteCommand;
    }

    private Command CreateGetCommand()
    {
        var getCommand = new Command("get", "Retrieve the value of a key")
        {
            _keyArgument,
            _routingAddress,
        };

        getCommand.SetHandler(async (key, routingAddress) =>
        {
            var client = CreateRoutingClient(routingAddress);
            var value = await client.GetValue<string>(key);
            Console.WriteLine(value);
        }, _keyArgument, _routingAddress);

        return getCommand;
    }

    private Command CreateSetCommand()
    {
        // TODO: it would be nice to be able to set data type as part of this CLI
        var keyValuePairArgument = new Argument<string>("keyValue", "The key-value pair in the format key=value");
        var setCommand = new Command("set", "Set a key-value pair")
        {
            keyValuePairArgument,
            _routingAddress
        };

        setCommand.SetHandler(async (kvp, routingAddress) =>
        {
            var parts = kvp.Split('=', 2);
            if (parts.Length != 2)
            {
                Console.WriteLine("ERROR: Please use the format key=value");
                return;
            }

            var key = parts[0];
            var value = parts[1];

            var client = CreateRoutingClient(routingAddress);
            var updatedValue = await client.SetValue(key, value);
            Console.WriteLine(updatedValue);
        }, keyValuePairArgument, _routingAddress);

        return setCommand;
    }

    private Command CreateListCommand()
    {
        var listCommand = new Command("list", "List all key value pairs")
        {
            _routingAddress
        };

        listCommand.SetHandler(async (routingAddress) =>
        {
            var client = CreateRoutingClient(routingAddress);
            var values = await client.GetAllValues();
            foreach (var value in values)
            {
                Console.WriteLine($"{value.Key}: {value.Value}");
            }
        }, _routingAddress);

        return listCommand;
    }

    private static RoutingClient CreateRoutingClient(string routingAddress)
    {
        return new RoutingClient(routingAddress);
    }
}
