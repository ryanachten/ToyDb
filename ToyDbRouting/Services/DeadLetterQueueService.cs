using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ToyDbRouting.Extensions;
using ToyDbRouting.Models;

namespace ToyDbRouting.Services;

public class DeadLetterQueueService : BackgroundService
{
    private readonly ConcurrentQueue<FailedWrite> _queue = new();
    private readonly DeadLetterOptions _options;
    private readonly ILogger<DeadLetterQueueService> _logger;

    public DeadLetterQueueService(
        ILogger<DeadLetterQueueService> logger,
        IOptions<RoutingOptions> routingOptions)
    {
        _logger = logger;
        _options = routingOptions.Value.DeadLetterOptions;

        if (_options.ProcessingIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(routingOptions),
                "DeadLetterOptions.ProcessingIntervalSeconds must be greater than zero.");
        }
    }

    public int QueueDepth => _queue.Count;

    public void Enqueue(FailedWrite failedWrite)
    {
        _queue.Enqueue(failedWrite);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.ProcessingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stoppingToken);
            await ProcessQueueAsync(stoppingToken);
        }
    }

    internal async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        var itemsToProcess = _queue.Count;
        if (itemsToProcess == 0) return;

        _logger.LogInformation("Processing {Count} items from dead-letter queue", itemsToProcess);

        for (var i = 0; i < itemsToProcess && !stoppingToken.IsCancellationRequested; i++)
        {
            if (!_queue.TryDequeue(out var failedWrite)) break;

            try
            {
                await RetryHelper.ExecuteWithRetryAsync(
                    () => failedWrite.Operation(failedWrite.Replica, 0),
                    _options.RetryOptions,
                    _logger,
                    $"DLQ replay {failedWrite.OperationType} for key {failedWrite.Key} on {failedWrite.Replica.Address}");

                _logger.LogInformation(
                    "Dead-letter retry succeeded for key {Key} on replica {Replica} (attempt {Attempt})",
                    failedWrite.Key, failedWrite.Replica.Address, failedWrite.RetryCount + 1);
            }
            catch (Exception ex)
            {
                failedWrite.RetryCount++;

                if (failedWrite.RetryCount >= _options.MaxRetries)
                {
                    _logger.LogError(
                        "Dead-letter retry exhausted for key {Key} on replica {Replica} after {Attempts} attempts: {Error}. Discarding.",
                        failedWrite.Key, failedWrite.Replica.Address, failedWrite.RetryCount, ex.Message);
                }
                else
                {
                    _logger.LogWarning(
                        "Dead-letter retry failed for key {Key} on replica {Replica} (attempt {Attempt}/{MaxRetries}): {Error}. Re-enqueuing.",
                        failedWrite.Key, failedWrite.Replica.Address, failedWrite.RetryCount, _options.MaxRetries, ex.Message);
                    _queue.Enqueue(failedWrite);
                }
            }
        }
    }
}
