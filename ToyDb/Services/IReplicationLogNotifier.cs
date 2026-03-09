using ToyDb.Models;

namespace ToyDb.Services;

public interface IReplicationLogNotifier
{
    void Publish(WalEntry entry);
    IReplicationLogSubscription Subscribe();
}
