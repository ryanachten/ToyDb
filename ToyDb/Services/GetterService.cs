using Grpc.Core;

namespace ToyDb.Services;

public class GetterService : Getter.GetterBase
{
    public override Task<GetResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetResponse() { Value = "World"});
    }
}