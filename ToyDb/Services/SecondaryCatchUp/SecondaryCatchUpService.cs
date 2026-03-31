using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ToyDb.Caches;
using ToyDb.Extensions;
using ToyDb.Models;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDbContracts.Data;

namespace ToyDb.Services.SecondaryCatchUp;

public class SecondaryCatchUpService(
    ILsnProvider lsnProvider,
    IDataStoreRepository storeRepository,
    IWriteAheadLogRepository walRepository,
    IKeyOffsetCache keyOffsetCache,
    IKeyEntryCache keyEntryCache,
    IOptions<ReplicaOptions> replicaOptions,
    ILogger<SecondaryCatchUpService> logger
) : BackgroundService
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (replicaOptions.Value.Role != ReplicaRole.Secondary)
        {
            logger.LogInformation("Node is not a secondary, skipping catch-up");
            return;
        }

        var primaryAddress = replicaOptions.Value.PrimaryAddress;
        if (string.IsNullOrWhiteSpace(primaryAddress))
        {
            logger.LogWarning("Primary address not configured, skipping catch-up");
            return;
        }

        var timer = logger.StartTimedLog(nameof(ExecuteAsync));

        try
        {
            await CatchUpWithRetryAsync(primaryAddress, stoppingToken);
        }
        finally
        {
            timer.Stop();
        }
    }

    private async Task CatchUpWithRetryAsync(string primaryAddress, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
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
            catch (Exception ex) when (attempt < MaxRetryAttempts)
            {
                var delay = TimeSpan.FromTicks(BaseRetryDelay.Ticks * (1 << (attempt - 1)));
                logger.LogWarning(ex,
                    "Catch-up attempt {Attempt}/{MaxAttempts} failed, retrying in {Delay}",
                    attempt, MaxRetryAttempts, delay);
                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Catch-up from primary {PrimaryAddress} failed after {MaxAttempts} attempts, node will start without catch-up",
                    primaryAddress, MaxRetryAttempts);
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
