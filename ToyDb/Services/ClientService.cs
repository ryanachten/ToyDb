﻿using Grpc.Core;
using ToyDb.Messages;
using ToyDb.Models;

namespace ToyDb.Services;

/// <summary>
/// Communicates with GRPC client
/// </summary>
public class ClientService(IDataStorageService dataStorageService) : Data.DataBase
{
    public override Task<KeyValueResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var value = dataStorageService.GetValue(request.Key);
        var response = new KeyValueResponse()
        {
            Key = request.Key,
            Type = value.Type,
            Value = value.Data
        };
        return Task.FromResult(response);
    }

    public override Task<GetAllValuesReresponse> GetAllValues(GetAllValuesRequest request, ServerCallContext context)
    {
        var values = dataStorageService.GetValues();
        var keyValuePairs = values.Select(x => new KeyValueResponse()
        {
            Key = x.Key,
            Type = x.Value.Type,
            Value = x.Value.Data
        });

        var response = new GetAllValuesReresponse();
        response.Values.AddRange(keyValuePairs);

        return Task.FromResult(response);
    }

    public override Task<KeyValueResponse> SetValue(KeyValueRequest request, ServerCallContext context)
    {
        var result = dataStorageService.SetValue(request.Key, new DatabaseEntry() {
            Key = request.Key,
            Type = request.Type,
            Data = request.Value
        });

        var response = new KeyValueResponse()
        {
            Key = request.Key,
            Type = result.Type,
            Value = result.Data
        };

        return Task.FromResult(response);
    }

    public override Task<DeleteResponse> DeleteValue(DeleteRequest request, ServerCallContext context)
    {
        dataStorageService.DeleteValue(request.Key);

        return Task.FromResult(new DeleteResponse());
    }
}