using Grpc.Net.Client;
using ToyDbContracts.Data;
using ToyDbClient.Services;

namespace ToyDbClient.Clients;

public class RoutingClient
{
    private readonly Data.DataClient _routingClient;

    public RoutingClient(string routingAddress)
    {
        var channel = GrpcChannel.ForAddress(routingAddress);
        _routingClient = new Data.DataClient(channel);
    }

    public async Task<T> GetValue<T>(string key)
    {
        var response = await _routingClient.GetValueAsync(new GetRequest { Key = key });
        return DbSerializer.Deserialize<T>(response);
    }

    public async Task<T> SetValue<T>(string key, T value)
    {
        var keyValuePair = DbSerializer.Serialize(key, value);
        var response = await _routingClient.SetValueAsync(keyValuePair);
        return DbSerializer.Deserialize<T>(response);
    }

    public async Task DeleteValue(string key)
    {
        await _routingClient.DeleteValueAsync(new DeleteRequest()
        {
            Key = key
        });
    }

    public async Task<Dictionary<string, string>> GetAllValues()
    {
        var response = await _routingClient.GetAllValuesAsync(new GetAllValuesRequest());
        return response.Values.ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value.ToStringUtf8());
    }
}
