using Grpc.Core;
using Microsoft.Extensions.Options;
using ToyDbContracts.Election;

namespace ToyDb.Services;

/// <summary>
/// gRPC service that handles inter-node cluster communication for leader election.
/// Implements RequestVote and Heartbeat RPCs for Raft-like consensus algorithm.
/// </summary>
public class ClusterService(
    ReplicaState replicaState,
    ILsnProvider lsnProvider,
    IOptions<ClusterOptions> clusterOptions,
    ILogger<ClusterService> logger
) : Election.ElectionBase
{
    private readonly ClusterOptions _cluster = clusterOptions.Value;

    public override Task<VoteResponse> RequestVote(RequestVoteRequest request, ServerCallContext context)
    {
        var currentTerm = replicaState.CurrentTerm;

        if (request.Term < currentTerm)
        {
            logger.LogInformation(
                "Rejecting vote for {NodeId}: term {RequestTerm} < current {CurrentTerm}",
                request.NodeId, request.Term, currentTerm);
            return Task.FromResult(new VoteResponse { Granted = false, Term = currentTerm, NodeId = _cluster.NodeId });
        }

        if (request.Term > currentTerm)
        {
            replicaState.SetTerm(request.Term);
            replicaState.ResetVote();
            currentTerm = request.Term;
        }

        var votedFor = replicaState.VotedFor;
        if (votedFor != null && votedFor != request.NodeId)
        {
            logger.LogInformation(
                "Rejecting vote for {NodeId}: already voted for {VotedFor} in term {Term}",
                request.NodeId, votedFor, currentTerm);
            return Task.FromResult(new VoteResponse { Granted = false, Term = currentTerm, NodeId = _cluster.NodeId });
        }

        var localLsn = GetLocalLsn();
        if (request.LastLsn < localLsn)
        {
            logger.LogInformation(
                "Rejecting vote for {NodeId}: candidate LSN {CandidateLsn} < local LSN {LocalLsn}",
                request.NodeId, request.LastLsn, localLsn);
            return Task.FromResult(new VoteResponse { Granted = false, Term = currentTerm, NodeId = _cluster.NodeId });
        }

        replicaState.SetVotedFor(request.NodeId);

        logger.LogInformation(
            "Granted vote to {NodeId} for term {Term}",
            request.NodeId, currentTerm);

        return Task.FromResult(new VoteResponse { Granted = true, Term = currentTerm, NodeId = _cluster.NodeId });
    }

    public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        var currentTerm = replicaState.CurrentTerm;

        if (request.Term < currentTerm)
        {
            return Task.FromResult(new HeartbeatResponse { Term = currentTerm, Success = false });
        }

        if (request.Term > currentTerm)
        {
            replicaState.SetTerm(request.Term);
            replicaState.ResetVote();
        }

        replicaState.SetLeader(request.LeaderId, request.LeaderAddress);
        replicaState.UpdateHeartbeatReceived();

        return Task.FromResult(new HeartbeatResponse { Term = request.Term, Success = true });
    }

    public override Task<GetRoleResponse> GetRole(GetRoleRequest request, ServerCallContext context)
    {
        var role = replicaState.IsPrimary ? "Primary" : "Secondary";

        return Task.FromResult(new GetRoleResponse
        {
            Role = role,
            Term = replicaState.CurrentTerm,
            LeaderId = replicaState.LeaderId ?? string.Empty,
            LeaderAddress = replicaState.LeaderAddress ?? string.Empty
        });
    }

    private long GetLocalLsn()
    {
        return lsnProvider.Current;
    }
}
