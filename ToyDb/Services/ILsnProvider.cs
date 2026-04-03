namespace ToyDb.Services;

public interface ILsnProvider
{
    long Current { get; }

    long Next();

    void SyncTo(long lsn);
}
