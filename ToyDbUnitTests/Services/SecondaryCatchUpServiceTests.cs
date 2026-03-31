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

    [Fact]
    public async Task GivenPrimaryNode_WhenStarted_ThenSkipsCatchUp()
    {
        var options = Options.Create(new ReplicaOptions { Role = ReplicaRole.Primary });
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
        _storeRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
    }

    [Fact]
    public async Task GivenSecondaryWithNoPrimaryAddress_WhenStarted_ThenSkipsCatchUp()
    {
        var options = Options.Create(new ReplicaOptions
        {
            Role = ReplicaRole.Secondary,
            PrimaryAddress = null
        });
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
    }

    [Fact]
    public async Task GivenSecondaryWithEmptyPrimaryAddress_WhenStarted_ThenSkipsCatchUp()
    {
        var options = Options.Create(new ReplicaOptions
        {
            Role = ReplicaRole.Secondary,
            PrimaryAddress = ""
        });
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.Never);
    }

    [Fact]
    public async Task GivenPrimaryUnreachable_WhenStarted_ThenDoesNotThrow()
    {
        var options = Options.Create(new ReplicaOptions
        {
            Role = ReplicaRole.Secondary,
            PrimaryAddress = "https://nonexistent-host:9999"
        });
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(10000);
        await service.StopAsync(CancellationToken.None);

        _walRepositoryMock.Verify(r => r.GetLatestLsn(), Times.AtLeast(2));
    }

    private SecondaryCatchUpService CreateService(IOptions<ReplicaOptions> options)
    {
        return new SecondaryCatchUpService(
            _lsnProviderMock.Object,
            _storeRepositoryMock.Object,
            _walRepositoryMock.Object,
            _keyOffsetCacheMock.Object,
            _keyEntryCacheMock.Object,
            options,
            _loggerMock.Object);
    }
}
