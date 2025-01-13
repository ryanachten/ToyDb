using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using ToyDb.Messages;
using ToyDbClient.Services;

namespace ToyDbClient.Clients;

public class ReplicaClient
{
    private readonly Data.DataClient _dataClient;

    public ReplicaClient(string dbAddress)
    {
        var channel = GrpcChannel.ForAddress(dbAddress);
        _dataClient = new Data.DataClient(channel);
    }

    public async Task<T> GetValue<T>(string key)
    {
        var response = await _dataClient.GetValueAsync(new GetRequest { Key = key });
        return DbSerializer.Deserialize<T>(response);
    }

    public async Task<T> SetValue<T>(DateTime timestamp, string key, T value)
    {
        var keyValuePair = DbSerializer.Serialize(timestamp, key, value);
        var response = await _dataClient.SetValueAsync(keyValuePair);
        return DbSerializer.Deserialize<T>(response);
    }

    public async Task DeleteValue(DateTime timestamp, string key)
    {
        await _dataClient.DeleteValueAsync(new DeleteRequest()
        {
            Timestamp = Timestamp.FromDateTime(timestamp),
            Key = key
        });
    }

    public async Task<Dictionary<string, string>> GetAllValues()
    {
        var response = await _dataClient.GetAllValuesAsync(new GetAllValuesRequest());
        return response.Values.ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value.ToStringUtf8());
    }
}
