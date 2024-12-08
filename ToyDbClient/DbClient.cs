using Grpc.Net.Client;

namespace ToyDbClient;

internal class DbClient
{
    private readonly Getter.GetterClient _getterClient;
    private readonly Setter.SetterClient _setterClient;

    public DbClient(string dbAddress)
    {
        var channel = GrpcChannel.ForAddress(dbAddress);
        _getterClient = new Getter.GetterClient(channel);
        _setterClient = new Setter.SetterClient(channel);
    }

    public async Task<string> GetValue(string key)
    {
        var response = await _getterClient.GetValueAsync(new GetRequest { Key = key });
        return response.Value;
    }

    public async Task<string> SetValue(string key, string? value)
    {
        var response = await _setterClient.SetValueAsync(new SetRequest { Key = key, Value = value });
        return response.Value;
    }
}
