namespace ToyDb.Services;

public class ReplicaState
{
    private long _currentTerm;
    private string? _votedFor;
    private string? _leaderId;
    private string? _leaderAddress;
    private DateTime _lastHeartbeatReceived = DateTime.UtcNow;
    private bool _isPrimary;

    public long CurrentTerm => Interlocked.Read(ref _currentTerm);

    public string? VotedFor => _votedFor;

    public string? LeaderId => _leaderId;

    public string? LeaderAddress => _leaderAddress;

    public DateTime LastHeartbeatReceived => _lastHeartbeatReceived;

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
        _leaderId = leaderId;
        _leaderAddress = leaderAddress;
    }

    public void ResetVote()
    {
        _votedFor = null;
    }

    public void UpdateHeartbeatReceived()
    {
        _lastHeartbeatReceived = DateTime.UtcNow;
    }
}
