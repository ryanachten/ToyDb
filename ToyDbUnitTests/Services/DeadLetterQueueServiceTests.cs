using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ToyDbRouting.Clients;
using ToyDbRouting.Constants;
using ToyDbRouting.Models;
using ToyDbRouting.Services;

namespace ToyDbUnitTests.Services;

public class DeadLetterQueueServiceTests
{
    private readonly DeadLetterQueueService _service;
    private readonly Mock<IOptions<RoutingOptions>> _routingOptionsMock;

    public DeadLetterQueueServiceTests()
    {
        _routingOptionsMock = new Mock<IOptions<RoutingOptions>>();
        _routingOptionsMock.Setup(o => o.Value).Returns(new RoutingOptions
        {
            Partitions = [new() { PartitionId = "test", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = [] }],
            DeadLetterOptions = new DeadLetterOptions
            {
                MaxRetries = 3,
                ProcessingIntervalSeconds = 1,
                RetryOptions = new RetryOptions { MaxRetries = 0, BaseDelayMs = 10, MaxDelayMs = 100 }
            }
        });

        _service = new DeadLetterQueueService(
            new Mock<ILogger<DeadLetterQueueService>>().Object,
            _routingOptionsMock.Object);
    }

    [Fact]
    public async Task GivenEnqueuedWrite_WhenRetrySucceeds_ThenItemIsRemovedFromQueue()
    {
        // Arrange
        var replicaMock = new Mock<ReplicaClient>();
        var callCount = 0;

        _service.Enqueue(new FailedWrite
        {
            Key = "test-key",
            Replica = replicaMock.Object,
            PartitionId = "partition-1",
            OperationType = WriteOperationType.Write,
            Operation = (replica, index) =>
            {
                callCount++;
                return Task.CompletedTask;
            },
            FailedAt = DateTime.UtcNow
        });

        Assert.Equal(1, _service.QueueDepth);

        // Act
        await _service.ProcessQueueAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, _service.QueueDepth);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GivenEnqueuedWrite_WhenRetryExceedsMaxRetries_ThenItemIsDiscarded()
    {
        // Arrange
        var replicaMock = new Mock<ReplicaClient>();

        var failedWrite = new FailedWrite
        {
            Key = "test-key",
            Replica = replicaMock.Object,
            PartitionId = "partition-1",
            OperationType = WriteOperationType.Write,
            Operation = (replica, index) => throw new Exception("Persistent failure"),
            FailedAt = DateTime.UtcNow,
            RetryCount = 2 // Already retried twice, MaxRetries=3 so next failure will discard
        };

        _service.Enqueue(failedWrite);
        Assert.Equal(1, _service.QueueDepth);

        // Act
        await _service.ProcessQueueAsync(CancellationToken.None);

        // Assert — item should be discarded, not re-enqueued
        Assert.Equal(0, _service.QueueDepth);
    }

    [Fact]
    public async Task GivenEnqueuedWrite_WhenRetryFails_ThenItemIsReEnqueued()
    {
        // Arrange
        var replicaMock = new Mock<ReplicaClient>();

        _service.Enqueue(new FailedWrite
        {
            Key = "test-key",
            Replica = replicaMock.Object,
            PartitionId = "partition-1",
            OperationType = WriteOperationType.Write,
            Operation = (replica, index) => throw new Exception("Temporary failure"),
            FailedAt = DateTime.UtcNow,
            RetryCount = 0
        });

        Assert.Equal(1, _service.QueueDepth);

        // Act
        await _service.ProcessQueueAsync(CancellationToken.None);

        // Assert — item should be re-enqueued with incremented retry count
        Assert.Equal(1, _service.QueueDepth);
    }

    [Fact]
    public async Task GivenEmptyQueue_WhenProcessing_ThenCompletesWithoutErrors()
    {
        // Arrange — queue is empty

        // Act
        await _service.ProcessQueueAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, _service.QueueDepth);
    }
}
