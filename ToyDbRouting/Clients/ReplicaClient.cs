using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using ToyDbContracts.Data;

namespace ToyDbRouting.Clients;

public class ReplicaClient
{
    private readonly Data.DataClient _dataClient;

    // Only used for tests, TODO: update to handle this warning properly
    protected ReplicaClient() { }

    public ReplicaClient(string dbAddress)
    {
        var handler = new HttpClientHandler
        {
            // TODO: this is a hack - investigate properly
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var channel = GrpcChannel.ForAddress(dbAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        _dataClient = new Data.DataClient(channel);
    }

    public virtual async Task<KeyValueResponse> GetValue(string key)
    {
        return await _dataClient.GetValueAsync(new GetRequest { Key = key });
    }

    public virtual async Task<GetAllValuesResponse> GetAllValues()
    {
        return await _dataClient.GetAllValuesAsync(new GetAllValuesRequest());
    }

    public virtual async Task<KeyValueResponse> SetValue(KeyValueRequest keyValuePair)
    {
        return await _dataClient.SetValueAsync(keyValuePair);
    }

    public virtual async Task DeleteValue(DateTime timestamp, string key)
    {
        await _dataClient.DeleteValueAsync(new DeleteRequest
        {
            Timestamp = Timestamp.FromDateTime(timestamp),
            Key = key
        });
    }
}
