using Google.Protobuf;
using Grpc.Core;
using ToyDb.Messages;
using ToyDb.Models;
using ToyDb.Repositories;

namespace ToyDb.Services;

public class DataService(IDatabaseRepository databaseRepository) : Data.DataBase
{
    public override Task<KeyValueResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var value = databaseRepository.GetValue(request.Key);
        return Task.FromResult(new KeyValueResponse() {
            Key = request.Key,
            Type = value.Type,
            Value = ByteString.CopyFrom(value.Data)
        });
    }

    public override Task<GetAllValuesReresponse> GetAllValues(GetAllValuesRequest request, ServerCallContext context)
    {
        var values = databaseRepository.GetValues();
        var keyValuePairs = values.Select(x => new KeyValueResponse()
        {
            Key = x.Key,
            Type = x.Value.Type,
            Value = ByteString.CopyFrom(x.Value.Data)
        });

        var response = new GetAllValuesReresponse();
        response.Values.AddRange(keyValuePairs);

        return Task.FromResult(response);
    }

    public override Task<KeyValueResponse> SetValue(KeyValueRequest request, ServerCallContext context)
    {
        var result = databaseRepository.SetValue(request.Key, new DatabaseEntry() {
            Type = request.Type,
            Data = request.Value.ToByteArray()
        });

        return Task.FromResult(new KeyValueResponse()
        {
            Key = request.Key,
            Type = result.Type,
            Value = ByteString.CopyFrom(result.Data)
        });
    }
}