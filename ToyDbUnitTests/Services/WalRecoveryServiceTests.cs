using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;
using ToyDb.Caches;
using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;
using ToyDbContracts.Data;

namespace ToyDbUnitTests.Services;

public class WalRecoveryServiceTests
{
    private readonly Mock<IDataStoreRepository> _storeRepositoryMock;
    private readonly Mock<IWriteAheadLogRepository> _walRepositoryMock;
    private readonly Mock<IKeyOffsetCache> _keyOffsetCacheMock;
    private readonly Mock<IKeyEntryCache> _keyEntryCacheMock;
    private readonly Mock<ILogger<WalRecoveryService>> _loggerMock;
    private readonly WalRecoveryService _service;

    public WalRecoveryServiceTests()
    {
        _storeRepositoryMock = new Mock<IDataStoreRepository>();
        _walRepositoryMock = new Mock<IWriteAheadLogRepository>();
        _keyOffsetCacheMock = new Mock<IKeyOffsetCache>();
        _keyEntryCacheMock = new Mock<IKeyEntryCache>();
        _loggerMock = new Mock<ILogger<WalRecoveryService>>();

        _service = new WalRecoveryService(
            _storeRepositoryMock.Object,
            _walRepositoryMock.Object,
            _keyOffsetCacheMock.Object,
            _keyEntryCacheMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void GivenNoWalEntriesToReplay_WhenRecovering_ThenOnlyRebuildsIndex()
    {
        _storeRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(10);
        _walRepositoryMock.Setup(r => r.ReadFrom(11)).Returns(Enumerable.Empty<WalEntry>());
        _storeRepositoryMock.Setup(r => r.GetLatestEntries()).Returns(new Dictionary<string, (DatabaseEntry, long)>());

        _service.Recover();

        _walRepositoryMock.Verify(r => r.ReadFrom(11), Times.Once);
        _storeRepositoryMock.Verify(r => r.GetLatestEntries(), Times.Once);
        _keyOffsetCacheMock.Verify(c => c.Replace(It.IsAny<Dictionary<string, long>>()), Times.Once);
    }

    [Fact]
    public void GivenWalEntriesToReplay_WhenRecovering_ThenReplaysEntriesToDataStore()
    {
        var dataStoreLsn = 5;
        var walEntries = new[]
        {
            CreateWalEntry(6, "key1", false, "value1"),
            CreateWalEntry(7, "key2", false, "value2"),
            CreateWalEntry(8, "key3", true, null)
        };

        _storeRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(dataStoreLsn);
        _walRepositoryMock.Setup(r => r.ReadFrom(dataStoreLsn + 1)).Returns(walEntries);
        _storeRepositoryMock.Setup(r => r.Append(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DatabaseEntry>(), It.IsAny<bool>()))
            .Returns<long, string, DatabaseEntry, bool>((lsn, key, entry, isDelete) => lsn * 100);
        _storeRepositoryMock.Setup(r => r.GetLatestEntries()).Returns(new Dictionary<string, (DatabaseEntry, long)>());

        _service.Recover();

        _storeRepositoryMock.Verify(r => r.Append(6, "key1", It.Is<DatabaseEntry>(e => e.Key == "key1"), false), Times.Once);
        _storeRepositoryMock.Verify(r => r.Append(7, "key2", It.Is<DatabaseEntry>(e => e.Key == "key2"), false), Times.Once);
        _storeRepositoryMock.Verify(r => r.Append(8, "key3", It.IsAny<DatabaseEntry>(), true), Times.Once);
    }

    [Fact]
    public void GivenWalEntriesToReplay_WhenRecovering_ThenUpdatesCaches()
    {
        var dataStoreLsn = 3;
        var walEntries = new[]
        {
            CreateWalEntry(4, "key1", false, "value1"),
            CreateWalEntry(5, "key2", true, null)
        };

        _storeRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(dataStoreLsn);
        _walRepositoryMock.Setup(r => r.ReadFrom(dataStoreLsn + 1)).Returns(walEntries);
        _storeRepositoryMock.Setup(r => r.Append(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DatabaseEntry>(), It.IsAny<bool>()))
            .Returns<long, string, DatabaseEntry, bool>((lsn, key, entry, isDelete) => lsn * 100);
        _storeRepositoryMock.Setup(r => r.GetLatestEntries()).Returns(new Dictionary<string, (DatabaseEntry, long)>());

        _service.Recover();

        _keyOffsetCacheMock.Verify(c => c.Set("key1", 400), Times.Once);
        _keyEntryCacheMock.Verify(c => c.Set("key1", It.Is<DatabaseEntry>(e => e.Key == "key1")), Times.Once);
        _keyOffsetCacheMock.Verify(c => c.Remove("key2"), Times.Once);
        _keyEntryCacheMock.Verify(c => c.Remove("key2"), Times.Once);
    }

    [Fact]
    public void GivenWalEntriesWithDeletes_WhenRecovering_ThenRemovesFromCaches()
    {
        var dataStoreLsn = 1;
        var walEntries = new[]
        {
            CreateWalEntry(2, "key1", true, null)
        };

        _storeRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(dataStoreLsn);
        _walRepositoryMock.Setup(r => r.ReadFrom(dataStoreLsn + 1)).Returns(walEntries);
        _storeRepositoryMock.Setup(r => r.Append(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DatabaseEntry>(), It.IsAny<bool>()))
            .Returns(200);
        _storeRepositoryMock.Setup(r => r.GetLatestEntries()).Returns(new Dictionary<string, (DatabaseEntry, long)>());

        _service.Recover();

        _keyOffsetCacheMock.Verify(c => c.Remove("key1"), Times.Once);
        _keyEntryCacheMock.Verify(c => c.Remove("key1"), Times.Once);
        _keyOffsetCacheMock.Verify(c => c.Set(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    private static WalEntry CreateWalEntry(long lsn, string key, bool isDelete, string? value)
    {
        return new WalEntry
        {
            Lsn = lsn,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Key = key,
            Type = DataType.String,
            Data = value != null ? ByteString.CopyFromUtf8(value) : null,
            IsDelete = isDelete
        };
    }
}
