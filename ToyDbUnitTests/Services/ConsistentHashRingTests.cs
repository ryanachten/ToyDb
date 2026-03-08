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

    private ConsistentHashRing CreateRing(RoutingOptions options)
    {
        var optionsMock = new Mock<IOptions<RoutingOptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);
        return new ConsistentHashRing(optionsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void GetPartition_WithMultiplePartitions_DistributesKeys()
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
    public void GetPartition_WhenAddingPartition_MinimizesReshuffle()
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
    public void GetPartition_WrapAroundBehavior_Works()
    {
        // Arrange
        // We'll use a very small number of virtual nodes to make it easier to reason about,
        // though the actual hash values are hard to predict without calculating them.
        var options = new RoutingOptions
        {
            VirtualNodesPerPartition = 1,
            Partitions = [new PartitionConfiguration { PartitionId = "p1", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] }]
        };
        var ring = CreateRing(options);

        // Act & Assert
        // With only one partition, every key should map to it, regardless of its hash value.
        Assert.Equal("p1", ring.GetPartition("any-key").PartitionId);
        Assert.Equal("p1", ring.GetPartition("another-key").PartitionId);
        Assert.Equal("p1", ring.GetPartition("zzzzzz").PartitionId);
    }

    [Fact]
    public void GetPartition_WithEmptyRing_ThrowsInvalidOperationException()
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
}
