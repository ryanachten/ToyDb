using Grpc.Net.Client;
using ToyDbContracts.Election;

namespace ToyDbRouting.Clients;

public class ClusterClient
{
    public string Address { get; }
    private readonly Election.ElectionClient _electionClient;

    public ClusterClient(string address)
    {
        Address = address;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        _electionClient = new Election.ElectionClient(channel);
    }

    public async Task<NodeRole> GetRole(CancellationToken cancellationToken = default)
    {
        var response = await _electionClient.GetRoleAsync(new GetRoleRequest(), cancellationToken: cancellationToken);
        return new NodeRole
        {
            Role = response.Role,
            Term = response.Term,
            LeaderId = response.LeaderId,
            LeaderAddress = response.LeaderAddress
        };
    }
}

public class NodeRole
{
    public string Role { get; init; } = string.Empty;
    public long Term { get; init; }
    public string LeaderId { get; init; } = string.Empty;
    public string LeaderAddress { get; init; } = string.Empty;

    public bool IsPrimary => Role == "Primary";
}