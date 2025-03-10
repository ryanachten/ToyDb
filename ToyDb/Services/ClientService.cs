using Grpc.Core;
using ToyDb.Extensions;
using ToyDbContracts.Data;
using ToyDb.Models;

namespace ToyDb.Services;

/// <summary>
/// Communicates with GRPC client
/// </summary>
public class ClientService(
    IReadStorageService readStorageService,
    IWriteStorageService writeStorageService,
    ILogger<ClientService> logger
) : Data.DataBase
{
    public override Task<KeyValueResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(GetValue), request.Key);

        var value = readStorageService.GetValue(request.Key);
        var response = new KeyValueResponse()
        {
            Key = request.Key,
            Type = value.Type,
            Value = value.Data
        };

        timer.Stop();

        return Task.FromResult(response);
    }

    public override Task<GetAllValuesResponse> GetAllValues(GetAllValuesRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(GetAllValues));
        
        var values = readStorageService.GetValues();
        var keyValuePairs = values.Select(x => new KeyValueResponse()
        {
            Key = x.Key,
            Type = x.Value.Type,
            Value = x.Value.Data
        });

        var response = new GetAllValuesResponse();
        response.Values.AddRange(keyValuePairs);

        timer.Stop();

        return Task.FromResult(response);
    }

    public override async Task<KeyValueResponse> SetValue(KeyValueRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(SetValue), request.Key);

        await writeStorageService.SetValue(request.Key, new DatabaseEntry() {
            Timestamp = request.Timestamp,
            Key = request.Key,
            Type = request.Type,
            Data = request.Value
        });

        timer.Stop();

        return new KeyValueResponse()
        {
            Key = request.Key,
            Type = request.Type,
            Value = request.Value
        };
    }

    public override async Task<DeleteResponse> DeleteValue(DeleteRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(DeleteValue), request.Key);

        await writeStorageService.DeleteValue(request.Key);

        timer.Stop();

        return new DeleteResponse();
    }
}