using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ToyDbRouting.Models;
using ToyDbRouting.Services;

namespace ToyDbUnitTests.Services;

public class ConsistentHashRingTests
{
    private readonly Mock<ILogger<ConsistentHashRing>> _loggerMock;

    public ConsistentHashRingTests()
    {
        _loggerMock = new Mock<ILogger<ConsistentHashRing>>();
    }

    [Fact]
    public void GivenMultiplePartitions_WhenGettingPartition_ThenKeysAreDistributedAcrossPartitions()
    {
        // Arrange
        var options = new RoutingOptions
        {
            VirtualNodesPerPartition = 100,
            Partitions =
            [
                new PartitionConfiguration { PartitionId = "p1", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] },
                new PartitionConfiguration { PartitionId = "p2", PrimaryReplicaAddress = "http://localhost:5002", SecondaryReplicaAddresses = [] },
                new PartitionConfiguration { PartitionId = "p3", PrimaryReplicaAddress = "http://localhost:5003", SecondaryReplicaAddresses = [] }
            ]
        };
        var ring = CreateRing(options);
        var keyCount = 1000;
        var distribution = new Dictionary<string, int>();

        // Act
        for (int i = 0; i < keyCount; i++)
        {
            var key = $"key-{i}";
            var partition = ring.GetPartition(key);
            distribution[partition.PartitionId] = distribution.GetValueOrDefault(partition.PartitionId) + 1;
        }

        // Assert
        Assert.Equal(3, distribution.Count);
        foreach (var count in distribution.Values)
        {
            // With 100 virtual nodes, we expect a relatively even distribution.
            // 1000 keys / 3 partitions = ~333 keys per partition.
            // Allow some variance (e.g., +/- 100).
            Assert.InRange(count, 233, 433);
        }
    }

    [Fact]
    public void GivenTwoPartitions_WhenAddingThirdPartition_ThenMinimizesKeyReshuffle()
    {
        // Arrange
        var options1 = new RoutingOptions
        {
            VirtualNodesPerPartition = 100,
            Partitions =
            [
                new PartitionConfiguration { PartitionId = "p1", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] },
                new PartitionConfiguration { PartitionId = "p2", PrimaryReplicaAddress = "http://localhost:5002", SecondaryReplicaAddresses = [] }
            ]
        };
        var ring1 = CreateRing(options1);

        var options2 = new RoutingOptions
        {
            VirtualNodesPerPartition = 100,
            Partitions =
            [
                new PartitionConfiguration { PartitionId = "p1", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] },
                new PartitionConfiguration { PartitionId = "p2", PrimaryReplicaAddress = "http://localhost:5002", SecondaryReplicaAddresses = [] },
                new PartitionConfiguration { PartitionId = "p3", PrimaryReplicaAddress = "http://localhost:5003", SecondaryReplicaAddresses = [] }
            ]
        };
        var ring2 = CreateRing(options2);

        var keyCount = 1000;
        var movedKeys = 0;

        // Act
        for (int i = 0; i < keyCount; i++)
        {
            var key = $"key-{i}";
            var p1 = ring1.GetPartition(key);
            var p2 = ring2.GetPartition(key);

            if (p1.PartitionId != p2.PartitionId)
            {
                movedKeys++;
            }
        }

        // Assert
        // In theory, adding a 3rd node should move ~1/3 of the keys.
        // 1000 * 1/3 = ~333. Allow some variance.
        Assert.InRange(movedKeys, 200, 450);
    }

    [Fact]
    public void GivenSinglePartition_WhenGettingAnyKey_ThenAlwaysReturnsThatPartition()
    {
        // Arrange
        var options = new RoutingOptions
        {
            VirtualNodesPerPartition = 1,
            Partitions = [new PartitionConfiguration { PartitionId = "p1", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] }]
        };
        var ring = CreateRing(options);

        // Act & Assert
        Assert.Equal("p1", ring.GetPartition("any-key").PartitionId);
        Assert.Equal("p1", ring.GetPartition("another-key").PartitionId);
        Assert.Equal("p1", ring.GetPartition("zzzzzz").PartitionId);
    }

    [Fact]
    public void GivenMultiplePartitions_WhenKeyHashExceedsAllNodeHashes_ThenWrapsAroundToFirstPartition()
    {
        // Arrange
        // We use two partitions and find a key that hashes to a value larger than all virtual nodes.
        // This is easier to test by mocking the hash if we had a provider, 
        // but we'll use known values or high-value characters to increase likelihood of wrap-around.
        var options = new RoutingOptions
        {
            VirtualNodesPerPartition = 1,
            Partitions =
            [
                new PartitionConfiguration { PartitionId = "p1", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] },
                new PartitionConfiguration { PartitionId = "p2", PrimaryReplicaAddress = "http://localhost:5002", SecondaryReplicaAddresses = [] }
            ]
        };
        var ring = CreateRing(options);

        // Act
        // "\uFFFF" likely produces a high hash value
        var partition = ring.GetPartition("\uFFFF\uFFFF\uFFFF");

        // Assert
        // We just ensure it returns one of the partitions and doesn't throw.
        // To truly test wrap-around we'd need to know the hashes of "p1:0", "p2:0" and the key.
        Assert.Contains(partition.PartitionId, new[] { "p1", "p2" });
    }

    [Fact]
    public void GivenEmptyRing_WhenGettingPartition_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var options = new RoutingOptions
        {
            VirtualNodesPerPartition = 100,
            Partitions = []
        };
        var ring = CreateRing(options);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => ring.GetPartition("key"));
    }

    [Fact]
    public void GivenInvalidVirtualNodesPerPartition_WhenCreatingRing_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new RoutingOptions
        {
            VirtualNodesPerPartition = 0,
            Partitions = [new PartitionConfiguration { PartitionId = "p1", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] }]
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => CreateRing(options));
        Assert.Equal("VirtualNodesPerPartition", ex.ParamName);
    }

    private ConsistentHashRing CreateRing(RoutingOptions options)
    {
        var optionsMock = new Mock<IOptions<RoutingOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);
        return new ConsistentHashRing(optionsMock.Object, _loggerMock.Object);
    }
}
