using ToyDb.Repositories.WriteAheadLogRepository;

namespace ToyDb.Services;

/// <summary>
/// Manages the reading and incrementing of the log sequence number
/// to assist in crash recovery and secondary catch up
/// </summary>
/// <param name="walRepository"></param>
public class LsnProvider(IWriteAheadLogRepository walRepository) : ILsnProvider
{
    private long _current = walRepository.GetLatestLsn();

    public long Next() => Interlocked.Increment(ref _current);

    public void SyncTo(long lsn)
    {
        Interlocked.Exchange(ref _current, lsn);
    }
}
