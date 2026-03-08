using Moq;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;

namespace ToyDbUnitTests.Services;

public class LsnProviderTests
{
    [Fact]
    public void GivenFreshNode_WhenGettingNextLsn_ThenReturnsOne()
    {
        var walRepositoryMock = new Mock<IWriteAheadLogRepository>();
        walRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(0);

        var provider = new LsnProvider(walRepositoryMock.Object);

        var lsn = provider.Next();

        Assert.Equal(1, lsn);
    }

    [Fact]
    public void GivenExistingWal_WhenGettingNextLsn_ThenReturnsIncrementedValue()
    {
        var walRepositoryMock = new Mock<IWriteAheadLogRepository>();
        walRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(42);

        var provider = new LsnProvider(walRepositoryMock.Object);

        var lsn = provider.Next();

        Assert.Equal(43, lsn);
    }

    [Fact]
    public void GivenMultipleCalls_WhenGettingNextLsn_ThenReturnsMonotonicallyIncreasingValues()
    {
        var walRepositoryMock = new Mock<IWriteAheadLogRepository>();
        walRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(10);

        var provider = new LsnProvider(walRepositoryMock.Object);

        var lsn1 = provider.Next();
        var lsn2 = provider.Next();
        var lsn3 = provider.Next();

        Assert.Equal(11, lsn1);
        Assert.Equal(12, lsn2);
        Assert.Equal(13, lsn3);
        Assert.True(lsn1 < lsn2);
        Assert.True(lsn2 < lsn3);
    }

    [Fact]
    public async Task GivenConcurrentCalls_WhenGettingNextLsn_ThenReturnsUniqueValues()
    {
        var walRepositoryMock = new Mock<IWriteAheadLogRepository>();
        walRepositoryMock.Setup(r => r.GetLatestLsn()).Returns(100);

        var provider = new LsnProvider(walRepositoryMock.Object);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => provider.Next()))
            .ToArray();

        await Task.WhenAll(tasks);

        var lsns = tasks.Select(t => t.Result).ToList();
        var uniqueLsns = lsns.Distinct().ToList();

        Assert.Equal(100, uniqueLsns.Count);
        Assert.All(lsns, lsn => Assert.True(lsn > 100 && lsn <= 200));
    }
}
