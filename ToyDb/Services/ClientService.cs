using Google.Protobuf;
using Grpc.Core;
using ToyDb.Messages;
using ToyDb.Models;
using ToyDb.Repositories;

namespace ToyDb.Services;

/// <summary>
/// Communicates with GRPC client
/// </summary>
public class ClientService(IDatabaseRepository databaseRepository) : Data.DataBase
{
    public override async Task<KeyValueResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var value = await databaseRepository.GetValue(request.Key, context.CancellationToken);
        return new KeyValueResponse() {
            Key = request.Key,
            Type = value.Type,
            Value = value.Data
        };
    }

    public override async Task<GetAllValuesReresponse> GetAllValues(GetAllValuesRequest request, ServerCallContext context)
    {
        var values = await databaseRepository.GetValues(context.CancellationToken);
        var keyValuePairs = values.Select(x => new KeyValueResponse()
        {
            Key = x.Key,
            Type = x.Value.Type,
            Value = x.Value.Data
        });

        var response = new GetAllValuesReresponse();
        response.Values.AddRange(keyValuePairs);

        return response;
    }

    public override async Task<KeyValueResponse> SetValue(KeyValueRequest request, ServerCallContext context)
    {
        var result = await databaseRepository.SetValue(request.Key, new DatabaseEntry() {
            Key = request.Key,
            Type = request.Type,
            Data = request.Value
        }, context.CancellationToken);

        return new KeyValueResponse()
        {
            Key = request.Key,
            Type = result.Type,
            Value = result.Data
        };
    }
}