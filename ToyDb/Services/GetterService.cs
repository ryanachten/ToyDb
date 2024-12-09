using Grpc.Core;
using ToyDb.Repositories;

namespace ToyDb.Services;

public class GetterService(IDatabaseRepository databaseRepository) : Getter.GetterBase
{
    public override Task<GetResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var value = databaseRepository.GetValue(request.Key);
        return Task.FromResult(new GetResponse() { Value = value });
    }

    public override Task<GetAllValuesReresponse> GetAllValues(GetAllValuesRequest request, ServerCallContext context)
    {
        var values = databaseRepository.GetValues();
        var keyValuePairs = values.Select(x => new KeyValuePair()
        {
            Key = x.Key,
            Value = x.Value
        });

        var response = new GetAllValuesReresponse();
        response.Values.AddRange(keyValuePairs);

        return Task.FromResult(response);
    }
}