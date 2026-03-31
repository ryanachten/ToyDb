namespace ToyDb.Services;

public interface ILsnProvider
{
    long Next();

    void SyncTo(long lsn);
}
