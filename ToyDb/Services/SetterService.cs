using Grpc.Core;
using ToyDb.Repositories;

namespace ToyDb.Services;

public class SetterService(IDatabaseRepository databaseRepository) : Setter.SetterBase
{
    public override Task<SetResponse> SetValue(SetRequest request, ServerCallContext context)
    {
        var updatedValue = databaseRepository.SetValue(request.Key, request.Value);
        var protoSafeValue = updatedValue ?? string.Empty; // Protobuf has no concept of null
        return Task.FromResult(new SetResponse { Value = protoSafeValue });
    }
}