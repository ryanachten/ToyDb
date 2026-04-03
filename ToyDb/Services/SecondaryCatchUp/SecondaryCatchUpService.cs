using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ToyDb.Caches;
using ToyDb.Extensions;
using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDbContracts.Data;

namespace ToyDb.Services.SecondaryCatchUp;

/// <summary>
/// Background service that handles secondary catch-up from the primary replica.
/// Monitors leader changes, connects to the current primary, and replays WAL entries to catch up on missed writes.
/// </summary>
public class SecondaryCatchUpService(
    ILsnProvider lsnProvider,
    IDataStoreRepository storeRepository,
    IWriteAheadLogRepository walRepository,
    IKeyOffsetCache keyOffsetCache,
    IKeyEntryCache keyEntryCache,
    ReplicaState replicaState,
    IOptions<ReplicaOptions> replicaOptions,
    IOptions<ClusterOptions> clusterOptions,
    IOptions<SecondaryCatchUpOptions> catchUpOptions,
    ILogger<SecondaryCatchUpService> logger
) : BackgroundService
{
    private readonly int _maxRetryAttempts = catchUpOptions.Value.MaxRetryAttempts;
    private readonly TimeSpan _baseRetryDelay = TimeSpan.FromSeconds(catchUpOptions.Value.BaseRetryDelaySeconds);
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(catchUpOptions.Value.ReconnectDelaySeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (replicaOptions.Value.Role != ReplicaRole.Secondary && !replicaState.IsPrimary)
        {
            logger.LogInformation("Node is a secondary, will monitor for leader changes");
        }
        else if (replicaState.IsPrimary)
        {
            logger.LogInformation("Node is primary, skipping catch-up");
            return;
        }

        var currentPrimaryAddress = GetPrimaryAddress();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (replicaState.IsPrimary)
            {
                logger.LogInformation("Node became primary, stopping catch-up");
                return;
            }

            var primaryAddress = GetPrimaryAddress();

            if (string.IsNullOrWhiteSpace(primaryAddress))
            {
                logger.LogWarning("No primary address available, waiting for leader election");
                await Task.Delay(_reconnectDelay, stoppingToken);
                continue;
            }

            if (primaryAddress != currentPrimaryAddress)
            {
                logger.LogInformation(
                    "Primary changed from {OldPrimary} to {NewPrimary}, reconnecting",
                    currentPrimaryAddress, primaryAddress);
                currentPrimaryAddress = primaryAddress;
            }

            try
            {
                await CatchUpWithRetryAsync(primaryAddress, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Catch-up cancelled");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Replication stream from {PrimaryAddress} ended, will reconnect",
                    primaryAddress);
                await Task.Delay(_reconnectDelay, stoppingToken);
            }
        }
    }

    private string? GetPrimaryAddress()
    {
        if (!string.IsNullOrWhiteSpace(replicaState.LeaderAddress))
        {
            return replicaState.LeaderAddress;
        }

        return replicaOptions.Value.PrimaryAddress;
    }

    private async Task CatchUpWithRetryAsync(string primaryAddress, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= _maxRetryAttempts; attempt++)
        {
            try
            {
                await DrainFromPrimaryAsync(primaryAddress, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Catch-up cancelled");
                return;
            }
            catch (Exception ex) when (attempt < _maxRetryAttempts)
            {
                var delay = TimeSpan.FromTicks(_baseRetryDelay.Ticks * (1 << (attempt - 1)));
                logger.LogWarning(ex,
                    "Catch-up attempt {Attempt}/{MaxAttempts} failed, retrying in {Delay}",
                    attempt, _maxRetryAttempts, delay);
                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Catch-up from primary {PrimaryAddress} failed after {MaxAttempts} attempts",
                    primaryAddress, _maxRetryAttempts);
                throw;
            }
        }
    }

    private async Task DrainFromPrimaryAsync(string primaryAddress, CancellationToken stoppingToken)
    {
        var localWalLsn = walRepository.GetLatestLsn();
        var localStoreLsn = storeRepository.GetLatestLsn();
        var localLsn = Math.Max(localWalLsn, localStoreLsn);

        logger.LogInformation(
            "Starting catch-up from primary {PrimaryAddress}, local LSN: {LocalLsn}",
            primaryAddress, localLsn);

        var fromLsn = localLsn + 1;

        HttpClientHandler? handler = null;
        GrpcChannel? channel = null;
        try
        {
            handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            channel = GrpcChannel.ForAddress(primaryAddress, new GrpcChannelOptions
            {
                HttpHandler = handler
            });

            var client = new Data.DataClient(channel);
            var request = new StreamReplicationLogRequest { FromLsn = fromLsn };

            using var call = client.StreamReplicationLog(request, cancellationToken: stoppingToken);
            var entriesApplied = 0;
            var lastLsn = localLsn;

            while (await call.ResponseStream.MoveNext(stoppingToken))
            {
                var entry = call.ResponseStream.Current;

                if (entry.Lsn <= lastLsn)
                {
                    logger.LogWarning("Skipping duplicate LSN {Lsn}", entry.Lsn);
                    continue;
                }

                if (entry.Lsn > lastLsn + 1)
                {
                    logger.LogWarning(
                        "LSN gap detected: expected {Expected}, received {Received}",
                        lastLsn + 1, entry.Lsn);
                }

                ApplyEntryLocally(entry);
                lastLsn = entry.Lsn;
                entriesApplied++;
            }

            if (entriesApplied > 0)
            {
                lsnProvider.SyncTo(lastLsn);
            }

            logger.LogInformation(
                "Catch-up complete, applied {EntriesApplied} entries, final LSN: {FinalLsn}",
                entriesApplied, entriesApplied > 0 ? lastLsn : localLsn);
        }
        finally
        {
            channel?.Dispose();
            handler?.Dispose();
        }
    }

    private void ApplyEntryLocally(ReplicationLogEntry entry)
    {
        var databaseEntry = new DatabaseEntry
        {
            Timestamp = entry.Timestamp,
            Key = entry.Key,
            Type = entry.Type,
            Data = entry.Value.IsEmpty ? null : entry.Value
        };

        walRepository.Append(entry.Lsn, entry.Key, databaseEntry, entry.IsDelete);
        var offset = storeRepository.Append(entry.Lsn, entry.Key, databaseEntry, entry.IsDelete);

        if (entry.IsDelete)
        {
            keyOffsetCache.Remove(entry.Key);
            keyEntryCache.Remove(entry.Key);
        }
        else
        {
            keyOffsetCache.Set(entry.Key, offset);
            keyEntryCache.Set(entry.Key, databaseEntry);
        }
    }
}
