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
}