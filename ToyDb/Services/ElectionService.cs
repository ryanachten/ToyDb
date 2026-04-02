using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ToyDbContracts.Election;

namespace ToyDb.Services;

public class ElectionService(
    ReplicaState replicaState,
    ILsnProvider lsnProvider,
    IOptions<ClusterOptions> clusterOptions,
    IOptions<ReplicaOptions> replicaOptions,
    ILogger<ElectionService> logger
) : BackgroundService
{
    private readonly ClusterOptions _cluster = clusterOptions.Value;
    private readonly ReplicaOptions _replica = replicaOptions.Value;
    private readonly Random _random = new();

    private bool _isPrimary;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _isPrimary = _replica.Role == ReplicaRole.Primary;

        if (_isPrimary)
        {
            replicaState.SetIsPrimary(true);
            await RunPrimaryAsync(stoppingToken);
        }
        else
        {
            await RunSecondaryAsync(stoppingToken);
        }
    }

    private async Task RunPrimaryAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Node {NodeId} starting as primary for partition {PartitionId}, term {Term}",
            _cluster.NodeId, _cluster.PartitionId, replicaState.CurrentTerm);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SendHeartbeatsAsync(stoppingToken);
            await Task.Delay(_cluster.HeartbeatIntervalMs, stoppingToken);
        }
    }

    private async Task RunSecondaryAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Node {NodeId} starting as secondary for partition {PartitionId}",
            _cluster.NodeId, _cluster.PartitionId);

        while (!stoppingToken.IsCancellationRequested)
        {
            var primaryHealthy = await CheckPrimaryHealthAsync(stoppingToken);

            if (!primaryHealthy)
            {
                logger.LogWarning("Primary unhealthy, starting election");
                await RunElectionAsync(stoppingToken);
            }

            await Task.Delay(_cluster.HeartbeatIntervalMs, stoppingToken);
        }
    }

    private async Task<bool> CheckPrimaryHealthAsync(CancellationToken stoppingToken)
    {
        if (_isPrimary) return true;

        var timeSinceLastHeartbeat = DateTime.UtcNow - replicaState.LastHeartbeatReceived;
        if (timeSinceLastHeartbeat.TotalMilliseconds > _cluster.ElectionTimeoutMs)
        {
            logger.LogWarning(
                "No heartbeat from primary for {Elapsed}ms, primary considered unhealthy",
                timeSinceLastHeartbeat.TotalMilliseconds);
            return false;
        }

        return true;
    }

    private async Task RunElectionAsync(CancellationToken stoppingToken)
    {
        var jitter = _random.Next(-500, 500);
        var waitTime = _cluster.ElectionTimeoutMs + jitter;
        await Task.Delay(Math.Max(waitTime, 100), stoppingToken);

        var newTerm = replicaState.CurrentTerm + 1;
        replicaState.SetTerm(newTerm);
        replicaState.SetVotedFor(_cluster.NodeId);

        logger.LogInformation(
            "Node {NodeId} starting election for term {Term}",
            _cluster.NodeId, newTerm);

        var votesGranted = 1;
        var voteTasks = _cluster.PeerAddresses.Select(peer =>
            RequestVoteFromPeerAsync(peer, newTerm, stoppingToken));

        var results = await Task.WhenAll(voteTasks);
        votesGranted += results.Count(r => r);

        var majority = (_cluster.PeerAddresses.Count + 1) / 2 + 1;
        if (votesGranted >= majority)
        {
            logger.LogInformation(
                "Node {NodeId} won election with {Votes}/{Total} votes for term {Term}",
                _cluster.NodeId, votesGranted, _cluster.PeerAddresses.Count + 1, newTerm);
            _isPrimary = true;
            replicaState.SetIsPrimary(true);
        }
        else
        {
            logger.LogInformation(
                "Node {NodeId} lost election with {Votes}/{Total} votes for term {Term}",
                _cluster.NodeId, votesGranted, _cluster.PeerAddresses.Count + 1, newTerm);
            replicaState.ResetVote();
        }
    }

    private async Task<bool> RequestVoteFromPeerAsync(string peerAddress, long term, CancellationToken stoppingToken)
    {
        HttpClientHandler? handler = null;
        GrpcChannel? channel = null;
        try
        {
            handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            channel = GrpcChannel.ForAddress(peerAddress, new GrpcChannelOptions
            {
                HttpHandler = handler
            });

            var client = new Election.ElectionClient(channel);
            var request = new RequestVoteRequest
            {
                Term = term,
                NodeId = _cluster.NodeId,
                LastLsn = lsnProvider.Next() - 1
            };

            var response = await client.RequestVoteAsync(request, cancellationToken: stoppingToken);

            if (response.Term > replicaState.CurrentTerm)
            {
                replicaState.SetTerm(response.Term);
                replicaState.ResetVote();
                return false;
            }

            return response.Granted;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to request vote from {PeerAddress}", peerAddress);
            return false;
        }
        finally
        {
            channel?.Dispose();
            handler?.Dispose();
        }
    }

    private async Task SendHeartbeatsAsync(CancellationToken stoppingToken)
    {
        var heartbeatTasks = _cluster.PeerAddresses.Select(peer =>
            SendHeartbeatToPeerAsync(peer, stoppingToken));
        await Task.WhenAll(heartbeatTasks);
    }

    private async Task SendHeartbeatToPeerAsync(string peerAddress, CancellationToken stoppingToken)
    {
        HttpClientHandler? handler = null;
        GrpcChannel? channel = null;
        try
        {
            handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            channel = GrpcChannel.ForAddress(peerAddress, new GrpcChannelOptions
            {
                HttpHandler = handler
            });

            var client = new Election.ElectionClient(channel);
            var request = new HeartbeatRequest
            {
                Term = replicaState.CurrentTerm,
                LeaderId = _cluster.NodeId,
                CommitLsn = lsnProvider.Next() - 1
            };

            var response = await client.HeartbeatAsync(request, cancellationToken: stoppingToken);

            if (response.Term > replicaState.CurrentTerm)
            {
                logger.LogWarning(
                    "Peer {PeerAddress} has higher term {PeerTerm} > {CurrentTerm}, stepping down",
                    peerAddress, response.Term, replicaState.CurrentTerm);
                replicaState.SetTerm(response.Term);
                replicaState.ResetVote();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send heartbeat to {PeerAddress}", peerAddress);
        }
        finally
        {
            channel?.Dispose();
            handler?.Dispose();
        }
    }
}
