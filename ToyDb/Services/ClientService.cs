﻿using Grpc.Core;
using ToyDb.Messages;
using ToyDb.Models;

namespace ToyDb.Services;

/// <summary>
/// Communicates with GRPC client
/// </summary>
public class ClientService(
    IReadStorageService readStorageService,
    IWriteStorageService writeStorageService
) : Data.DataBase
{
    public override Task<KeyValueResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var value = readStorageService.GetValue(request.Key);
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
        var values = readStorageService.GetValues();
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

    public override async Task<KeyValueResponse> SetValue(KeyValueRequest request, ServerCallContext context)
    {
        await writeStorageService.SetValue(request.Key, new DatabaseEntry() {
            Key = request.Key,
            Type = request.Type,
            Data = request.Value
        });

        return new KeyValueResponse()
        {
            Key = request.Key,
            Type = request.Type,
            Value = request.Value
        };
    }

    public override async Task<DeleteResponse> DeleteValue(DeleteRequest request, ServerCallContext context)
    {
        await writeStorageService.DeleteValue(request.Key);

        return new DeleteResponse();
    }
}