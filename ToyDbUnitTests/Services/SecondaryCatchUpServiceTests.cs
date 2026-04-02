using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ToyDb.Caches;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;
using ToyDb.Services.SecondaryCatchUp;

namespace ToyDbUnitTests.Services;

public class SecondaryCatchUpServiceTests
{
    private readonly Mock<ILsnProvider> _lsnProviderMock = new();
    private readonly Mock<IDataStoreRepository> _storeRepositoryMock = new();
    private readonly Mock<IWriteAheadLogRepository> _walRepositoryMock = new();
    private readonly Mock<IKeyOffsetCache> _keyOffsetCacheMock = new();
    private readonly Mock<IKeyEntryCache> _keyEntryCacheMock = new();
    private readonly Mock<ILogger<SecondaryCatchUpService>> _loggerMock = new();
    private readonly ReplicaState _replicaState = new();

    [Fact]
    public async Task GivenPrimaryNode_WhenStarted_ThenSkipsCatchUp()
    {
        var replicaOptions = Options.Create(new ReplicaOptions { Role = ReplicaRole.Primary });
        var clusterOptions = Options.Create(CreateClusterOptions());
        _replicaState.SetIsPrimary(true);
        var service = CreateService(replicaOptions, clusterOptions);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
        _storeRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
    }

    [Fact]
    public async Task GivenSecondaryWithNoPrimaryAddress_WhenStarted_ThenWaitsForLeader()
    {
        var replicaOptions = Options.Create(new ReplicaOptions
        {
            Role = ReplicaRole.Secondary,
            PrimaryAddress = null
        });
        var clusterOptions = Options.Create(CreateClusterOptions());
        var service = CreateService(replicaOptions, clusterOptions);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
    }

    [Fact]
    public async Task GivenSecondaryWithEmptyPrimaryAddress_WhenStarted_ThenWaitsForLeader()
    {
        var replicaOptions = Options.Create(new ReplicaOptions
        {
            Role = ReplicaRole.Secondary,
            PrimaryAddress = ""
        });
        var clusterOptions = Options.Create(CreateClusterOptions());
        var service = CreateService(replicaOptions, clusterOptions);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
    }

    [Fact]
    public async Task GivenPrimaryUnreachable_WhenStarted_ThenDoesNotThrow()
    {
        var replicaOptions = Options.Create(new ReplicaOptions
        {
            Role = ReplicaRole.Secondary,
            PrimaryAddress = "https://nonexistent-host:9999"
        });
        var clusterOptions = Options.Create(CreateClusterOptions());
        var service = CreateService(replicaOptions, clusterOptions);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(10000);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.AtLeast(2));
    }

    private SecondaryCatchUpService CreateService(
        IOptions<ReplicaOptions> replicaOptions,
        IOptions<ClusterOptions> clusterOptions)
    {
        return new SecondaryCatchUpService(
            _lsnProviderMock.Object,
            _storeRepositoryMock.Object,
            _walRepositoryMock.Object,
            _keyOffsetCacheMock.Object,
            _keyEntryCacheMock.Object,
            _replicaState,
            replicaOptions,
            clusterOptions,
            _loggerMock.Object);
    }

    private static ClusterOptions CreateClusterOptions()
    {
        return new ClusterOptions
        {
            NodeId = "test-node",
            PartitionId = "p1",
            PeerAddresses = []
        };
    }
}
