using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using ToyDbRouting.Services;
using ToyDbContracts.Data;

namespace ToyDbRouting.Clients;

public class ReplicaClient
{
    private readonly Data.DataClient _dataClient;

    public ReplicaClient(string dbAddress)
    {
        var handler = new HttpClientHandler();

        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        var channel = GrpcChannel.ForAddress(dbAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        _dataClient = new Data.DataClient(channel);
    }

    public async Task<T> GetValue<T>(string key)
    {
        var response = await _dataClient.GetValueAsync(new GetRequest { Key = key });
        return DataSerializer.Deserialize<T>(response);
    }

    public async Task<T> SetValue<T>(string key, T value)
    {
        var keyValuePair = DataSerializer.Serialize(key, value);
        var response = await _dataClient.SetValueAsync(keyValuePair);
        return DataSerializer.Deserialize<T>(response);
    }

    public async Task DeleteValue(string key)
    {
        await DeleteValue(DateTime.UtcNow, key);
    }

    public async Task DeleteValue(DateTime timestamp, string key)
    {
        await _dataClient.DeleteValueAsync(new DeleteRequest
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

    // TODO: I really dislike these "raw" methods, especially if the non "raw" methods are only used by tests. Figure out something nicer 
    public async Task<KeyValueResponse> GetValueRaw(string key)
    {
        return await _dataClient.GetValueAsync(new GetRequest { Key = key });
    }

    public async Task<KeyValueResponse> SetValueRaw(KeyValueRequest keyValuePair)
    {
        return await _dataClient.SetValueAsync(keyValuePair);
    }

    public async Task<GetAllValuesResponse> GetAllValuesRaw()
    {
        return await _dataClient.GetAllValuesAsync(new GetAllValuesRequest());
    }
}
