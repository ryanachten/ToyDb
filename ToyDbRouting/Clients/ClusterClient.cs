using Grpc.Net.Client;
using ToyDbContracts.Election;

namespace ToyDbRouting.Clients;

public class ClusterClient : IDisposable
{
    public string Address { get; }
    private readonly GrpcChannel _channel;
    private readonly Election.ElectionClient _electionClient;

    public ClusterClient(string address)
    {
        Address = address;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        _electionClient = new Election.ElectionClient(_channel);
    }

    public async Task<NodeRole> GetRole(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var response = await _electionClient.GetRoleAsync(new GetRoleRequest(), cancellationToken: cts.Token);
        return new NodeRole
        {
            Role = response.Role,
            Term = response.Term,
            LeaderId = response.LeaderId,
            LeaderAddress = response.LeaderAddress
        };
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}

public class NodeRole
{
    public NodeRoleType Role { get; init; }
    public long Term { get; init; }
    public string LeaderId { get; init; } = string.Empty;
    public string LeaderAddress { get; init; } = string.Empty;

    public bool IsPrimary => Role == NodeRoleType.Primary;
}