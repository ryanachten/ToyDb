using Grpc.Core;

namespace ToyDb.Services;

public class SetterService : Setter.SetterBase
{
    public override Task<SetResponse> SetValue(SetRequest request, ServerCallContext context)
    {
        return Task.FromResult(new SetResponse() { Value = "World" });
    }
}