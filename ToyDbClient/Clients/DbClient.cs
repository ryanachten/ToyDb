﻿using Grpc.Net.Client;
using ToyDb.Messages;

namespace ToyDbClient.Clients;

internal class DbClient
{
    private readonly Data.DataClient _dataClient;

    public DbClient(string dbAddress)
    {
        var channel = GrpcChannel.ForAddress(dbAddress);
        _dataClient = new Data.DataClient(channel);
    }

    public async Task<T> GetValue<T>(string key)
    {
        var response = await _dataClient.GetValueAsync(new GetRequest { Key = key });
        return DbSerializer.Deserialize<T>(response);
    }

    public async Task<T> SetValue<T>(string key, T value)
    {
        var keyValuePair = DbSerializer.Serialize(key, value);
        var response = await _dataClient.SetValueAsync(keyValuePair);
        return DbSerializer.Deserialize<T>(response);
    }

    public async Task<Dictionary<string, string>> PrintAllValues()
    {
        var response = await _dataClient.GetAllValuesAsync(new GetAllValuesRequest());
        return response.Values.ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value.ToStringUtf8());
    }
}
