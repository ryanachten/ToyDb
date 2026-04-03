using Microsoft.Extensions.Options;

namespace ToyDb.Services;

public class ReplicaState(IOptions<ReplicaOptions> replicaOptions)
{
    private long _currentTerm;
    private volatile string? _votedFor;
    private volatile string? _leaderId;
    private volatile string? _leaderAddress;
    private long _lastHeartbeatTicks = DateTime.UtcNow.Ticks;
    private volatile bool _isPrimary = replicaOptions.Value.Role == ReplicaRole.Primary;
    private readonly object _leaderLock = new();

    public long CurrentTerm => Interlocked.Read(ref _currentTerm);

    public string? VotedFor => _votedFor;

    public string? LeaderId => _leaderId;

    public string? LeaderAddress => _leaderAddress;

    public DateTime LastHeartbeatReceived => new(Interlocked.Read(ref _lastHeartbeatTicks));

    public bool IsPrimary => _isPrimary;

    public void SetIsPrimary(bool isPrimary)
    {
        _isPrimary = isPrimary;
    }

    public bool TryIncrementTerm(long newTerm)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _currentTerm);
            if (newTerm <= current) return false;
            if (Interlocked.CompareExchange(ref _currentTerm, newTerm, current) == current)
                return true;
        }
    }

    public void SetTerm(long term)
    {
        Interlocked.Exchange(ref _currentTerm, term);
    }

    public void SetVotedFor(string? nodeId)
    {
        _votedFor = nodeId;
    }

    public void SetLeader(string? leaderId, string? leaderAddress)
    {
        lock (_leaderLock)
        {
            _leaderId = leaderId;
            _leaderAddress = leaderAddress;
        }
    }

    public void ResetVote()
    {
        _votedFor = null;
    }

    public void UpdateHeartbeatReceived()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
    }
}
