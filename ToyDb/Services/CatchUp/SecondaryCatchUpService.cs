using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ToyDb.Models;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDbContracts.Data;

namespace ToyDb.Services.CatchUp;

public class SecondaryCatchUpService(
    IWriteAheadLogRepository walRepository,
    IWriteStorageService writeStorageService,
    IOptions<ReplicationOptions> options,
    ILogger<SecondaryCatchUpService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(options.Value.PrimaryNodeAddress))
        {
            logger.LogInformation("No primary address configured — skipping catch-up");
            return;
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var channel = GrpcChannel.ForAddress(options.Value.PrimaryNodeAddress, new GrpcChannelOptions { HttpHandler = handler });
        var client = new Data.DataClient(channel);

        var delayMs = options.Value.RetryBaseDelayMs;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var myLastLsn = walRepository.GetLatestLsn();
                logger.LogInformation("Starting catch-up from LSN {Lsn} against primary {Primary}", myLastLsn + 1, options.Value.PrimaryNodeAddress);

                var stream = client.StreamReplicationLog(new StreamReplicationLogRequest { FromLsn = myLastLsn + 1 });

                var count = 0;
                await foreach (var entry in stream.ResponseStream.ReadAllAsync(stoppingToken))
                {
                    var dbEntry = new DatabaseEntry
                    {
                        Timestamp = entry.Timestamp,
                        Key = entry.Key,
                        Type = entry.Type,
                        Data = entry.Value ?? ByteString.Empty
                    };

                    if (entry.IsDelete)
                        await writeStorageService.DeleteValue(entry.Key);
                    else
                        await writeStorageService.SetValue(entry.Key, dbEntry);

                    count++;
                }

                logger.LogInformation("Replication stream ended. Applied {Count} entries", count);
                delayMs = options.Value.RetryBaseDelayMs;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Replication stream failed, retrying in {Delay}ms", delayMs);
                await Task.Delay(delayMs, stoppingToken);
                delayMs = Math.Min(delayMs * 2, options.Value.RetryMaxDelayMs);
            }
        }
    }
}
