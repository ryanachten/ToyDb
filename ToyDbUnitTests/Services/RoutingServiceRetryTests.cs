using System.Reflection;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ToyDbContracts.Data;
using ToyDbRouting.Clients;
using ToyDbRouting.Models;
using ToyDbRouting.Services;

namespace ToyDbUnitTests.Services;

public class RoutingServiceRetryTests
{
    private readonly Mock<IOptions<RoutingOptions>> _routingOptionsMock;
    private readonly Mock<ILogger<RoutingService>> _loggerMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<INtpService> _ntpServiceMock;
    private readonly RoutingService _service;

    public RoutingServiceRetryTests()
    {
        _routingOptionsMock = new Mock<IOptions<RoutingOptions>>();
        _loggerMock = new Mock<ILogger<RoutingService>>();
        _mapperMock = new Mock<IMapper>();
        _ntpServiceMock = new Mock<INtpService>();

        var routingOptions = new RoutingOptions
        {
            Partitions =
            [
                new() { PartitionId = "test", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = Array.Empty<string>() }
            ],
            PrimaryRetryOptions = new RetryOptions { MaxRetries = 2, BaseDelayMs = 10, MaxDelayMs = 100 },
            SecondaryRetryOptions = new RetryOptions { MaxRetries = 2, BaseDelayMs = 10, MaxDelayMs = 100 },
            CompletedSecondaryWritesThreshold = 1
        };
        _routingOptionsMock.Setup(o => o.Value).Returns(routingOptions);

        _service = new RoutingService(_routingOptionsMock.Object, _loggerMock.Object, _mapperMock.Object, _ntpServiceMock.Object);
    }

    [Fact]
    public async Task GivenPrimaryFailsOnce_WhenExecuteWithReplicaThresholdAsync_ThenSucceedsOnSecondAttempt()
    {
        // Arrange
        var primaryMock = new Mock<ReplicaClient>();
        var secondaryMock = new Mock<ReplicaClient>();
        var partition = CreateTestPartition(primaryMock, secondaryMock);

        var callCount = 0;
        primaryMock.Setup(p => p.SetValue(It.IsAny<KeyValueRequest>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1) throw new Exception("Primary failure");
                return new KeyValueResponse();
            });

        secondaryMock.Setup(s => s.SetValue(It.IsAny<KeyValueRequest>())).ReturnsAsync(new KeyValueResponse());

        // Act
        var result = await _service.ExecuteWithReplicaThresholdAsync(
            () => primaryMock.Object.SetValue(new KeyValueRequest()),
            (replica, index) => replica.SetValue(new KeyValueRequest()),
            partition,
            "testKey",
            RoutingService.OperationType.Write);

        // Assert
        Assert.NotNull(result.PrimaryResponse);
        Assert.Equal(2, result.ReplicasCompleted); // primary + 1 secondary
        Assert.Equal(2, result.ReplicasTotal);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task GivenPrimaryExhaustsRetries_WhenExecuteWithReplicaThresholdAsync_ThenThrowsException()
    {
        // Arrange
        var primaryMock = new Mock<ReplicaClient>();
        var partition = CreateTestPartition(primaryMock);

        primaryMock.Setup(p => p.SetValue(It.IsAny<KeyValueRequest>()))
            .ThrowsAsync(new Exception("Persistent failure"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _service.ExecuteWithReplicaThresholdAsync(
            () => primaryMock.Object.SetValue(new KeyValueRequest()),
            (replica, index) => Task.CompletedTask,
            partition,
            "testKey",
            RoutingService.OperationType.Write));
    }

    [Fact]
    public async Task GivenSecondaryFailsBelowThreshold_WhenExecuteWithReplicaThresholdAsync_ThenThrowsException()
    {
        // Arrange
        var primaryMock = new Mock<ReplicaClient>();
        var secondaryMock1 = new Mock<ReplicaClient>();
        var secondaryMock2 = new Mock<ReplicaClient>();
        var partition = CreateTestPartition(primaryMock, secondaryMock1, secondaryMock2);

        primaryMock.Setup(p => p.SetValue(It.IsAny<KeyValueRequest>())).ReturnsAsync(new KeyValueResponse());
        secondaryMock1.Setup(s => s.SetValue(It.IsAny<KeyValueRequest>())).ThrowsAsync(new Exception("Secondary failure"));
        secondaryMock2.Setup(s => s.SetValue(It.IsAny<KeyValueRequest>())).ThrowsAsync(new Exception("Secondary failure"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(() => _service.ExecuteWithReplicaThresholdAsync(
            () => primaryMock.Object.SetValue(new KeyValueRequest()),
            (replica, index) => replica.SetValue(new KeyValueRequest()),
            partition,
            "testKey",
            RoutingService.OperationType.Write));

        Assert.Contains("Failed to meet secondary write threshold", exception.Message);
    }

    [Fact]
    public async Task GivenPartialSecondarySuccess_WhenExecuteWithReplicaThresholdAsync_ThenWarningAdded()
    {
        // Arrange
        var primaryMock = new Mock<ReplicaClient>();
        var secondaryMock1 = new Mock<ReplicaClient>();
        var secondaryMock2 = new Mock<ReplicaClient>();
        var partition = CreateTestPartition(primaryMock, secondaryMock1, secondaryMock2);

        primaryMock.Setup(p => p.SetValue(It.IsAny<KeyValueRequest>())).ReturnsAsync(new KeyValueResponse());
        secondaryMock1.Setup(s => s.SetValue(It.IsAny<KeyValueRequest>())).ReturnsAsync(new KeyValueResponse());
        secondaryMock2.Setup(s => s.SetValue(It.IsAny<KeyValueRequest>())).ThrowsAsync(new Exception("Secondary failure"));

        // Act
        var result = await _service.ExecuteWithReplicaThresholdAsync(
            () => primaryMock.Object.SetValue(new KeyValueRequest()),
            (replica, index) => replica.SetValue(new KeyValueRequest()),
            partition,
            "testKey",
            RoutingService.OperationType.Write);

        // Assert
        Assert.Equal(2, result.ReplicasCompleted); // primary + 1 secondary
        Assert.Equal(3, result.ReplicasTotal);
        Assert.Single(result.Warnings);
        Assert.Contains("Partial success", result.Warnings[0]);
    }

    [Fact]
    public async Task GivenNoRetriesConfigured_WhenExecuteWithReplicaThresholdAsync_ThenFailsImmediately()
    {
        // Arrange
        var routingOptions = new RoutingOptions
        {
            Partitions =
            [
                new() { PartitionId = "test", PrimaryReplicaAddress = "http://localhost:5001", SecondaryReplicaAddresses = Array.Empty<string>() }
            ],
            PrimaryRetryOptions = new RetryOptions { MaxRetries = 0, BaseDelayMs = 10, MaxDelayMs = 100 },
            SecondaryRetryOptions = new RetryOptions { MaxRetries = 0, BaseDelayMs = 10, MaxDelayMs = 100 },
            CompletedSecondaryWritesThreshold = 1
        };
        _routingOptionsMock.Setup(o => o.Value).Returns(routingOptions);

        var service = new RoutingService(_routingOptionsMock.Object, _loggerMock.Object, _mapperMock.Object, _ntpServiceMock.Object);

        var primaryMock = new Mock<ReplicaClient>();
        var partition = CreateTestPartition(primaryMock);

        primaryMock.Setup(p => p.SetValue(It.IsAny<KeyValueRequest>()))
            .ThrowsAsync(new Exception("Failure"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => service.ExecuteWithReplicaThresholdAsync(
            () => primaryMock.Object.SetValue(new KeyValueRequest()),
            (replica, index) => Task.CompletedTask,
            partition,
            "testKey",
            RoutingService.OperationType.Write));
    }

    [Fact]
    public async Task GivenThresholdMetEarly_WhenExecuteWithReplicaThresholdAsync_ThenDoesNotWaitForRemaining()
    {
        // Arrange
        var primaryMock = new Mock<ReplicaClient>();
        var secondaryMock1 = new Mock<ReplicaClient>();
        var secondaryMock2 = new Mock<ReplicaClient>();
        var partition = CreateTestPartition(primaryMock, secondaryMock1, secondaryMock2);

        primaryMock.Setup(p => p.SetValue(It.IsAny<KeyValueRequest>())).ReturnsAsync(new KeyValueResponse());
        secondaryMock1.Setup(s => s.SetValue(It.IsAny<KeyValueRequest>())).ReturnsAsync(new KeyValueResponse());
        // secondaryMock2 will be called but not waited for since threshold=1 is met early

        // Act
        var result = await _service.ExecuteWithReplicaThresholdAsync(
            () => primaryMock.Object.SetValue(new KeyValueRequest()),
            (replica, index) => replica.SetValue(new KeyValueRequest()),
            partition,
            "testKey",
            RoutingService.OperationType.Write);

        // Assert
        Assert.Equal(2, result.ReplicasCompleted); // primary + 1 secondary
    }

    [Fact]
    public async Task GivenDeletePrimaryFailsOnce_WhenExecuteWithReplicaThresholdAsync_ThenSucceedsOnSecondAttempt()
    {
        // Arrange
        var primaryMock = new Mock<ReplicaClient>();
        var secondaryMock = new Mock<ReplicaClient>();
        var partition = CreateTestPartition(primaryMock, secondaryMock);

        var callCount = 0;
        primaryMock.Setup(p => p.DeleteValue(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1) throw new Exception("Delete failure");
                return Task.CompletedTask;
            });

        secondaryMock.Setup(s => s.DeleteValue(It.IsAny<DateTime>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        _ntpServiceMock.Setup(n => n.Now).Returns(DateTime.UtcNow);

        // Act
        var result = await _service.ExecuteWithReplicaThresholdAsync(
            async () =>
            {
                await primaryMock.Object.DeleteValue(_ntpServiceMock.Object.Now, "testKey");
                return 0;
            },
            (replica, index) => replica.DeleteValue(_ntpServiceMock.Object.Now, "testKey"),
            partition,
            "testKey",
            RoutingService.OperationType.Delete);

        // Assert
        Assert.Equal(0, result.PrimaryResponse);
        Assert.Equal(2, result.ReplicasCompleted);
        Assert.Equal(2, result.ReplicasTotal);
        Assert.Empty(result.Warnings);
    }


    private static Partition CreateTestPartition(Mock<ReplicaClient> primaryMock, params Mock<ReplicaClient>[] secondaryMocks)
    {
        var partition = new Partition(new PartitionConfiguration
        {
            PartitionId = "test",
            PrimaryReplicaAddress = "http://localhost:5001",
            SecondaryReplicaAddresses = secondaryMocks.Select((_, i) => $"http://localhost:{5002 + i}").ToArray()
        });

        // Use reflection to set the mocked replicas
        var primaryField = typeof(Partition).GetField("PrimaryReplica", BindingFlags.Public | BindingFlags.Instance);
        primaryField?.SetValue(partition, primaryMock.Object);

        var secondaryField = typeof(Partition).GetField("SecondaryReplicas", BindingFlags.Public | BindingFlags.Instance);
        var immutableArray = System.Collections.Immutable.ImmutableArray.CreateRange(secondaryMocks.Select(m => m.Object));
        secondaryField?.SetValue(partition, immutableArray);

        return partition;
    }
}