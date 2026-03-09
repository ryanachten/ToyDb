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
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.Value.PrimaryNodeAddress))
        {
            logger.LogInformation("No primary address configured — skipping catch-up");
            return;
        }

        var myLastLsn = walRepository.GetLatestLsn();
        logger.LogInformation("Starting catch-up from LSN {Lsn} against primary {Primary}", myLastLsn + 1, options.Value.PrimaryNodeAddress);

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var channel = GrpcChannel.ForAddress(options.Value.PrimaryNodeAddress, new GrpcChannelOptions { HttpHandler = handler });
        var client = new Data.DataClient(channel);

        var stream = client.StreamReplicationLog(new StreamReplicationLogRequest { FromLsn = myLastLsn + 1 });

        var count = 0;
        await foreach (var entry in stream.ResponseStream.ReadAllAsync(cancellationToken))
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

        logger.LogInformation("Catch-up complete. Applied {Count} entries", count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
