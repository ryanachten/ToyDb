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

        // TODO: this is a hack - investigate properly
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        var channel = GrpcChannel.ForAddress(dbAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        _dataClient = new Data.DataClient(channel);
    }

    public async Task<KeyValueResponse> GetValue(string key)
    {
        return await _dataClient.GetValueAsync(new GetRequest { Key = key });
    }

    public async Task<GetAllValuesResponse> GetAllValues()
    {
        return await _dataClient.GetAllValuesAsync(new GetAllValuesRequest());
    }

    public async Task<KeyValueResponse> SetValue(KeyValueRequest keyValuePair)
    {
        return await _dataClient.SetValueAsync(keyValuePair);
    }

    public async Task DeleteValue(DateTime timestamp, string key)
    {
        await _dataClient.DeleteValueAsync(new DeleteRequest
        {
            Timestamp = Timestamp.FromDateTime(timestamp),
            Key = key
        });
    }
}
