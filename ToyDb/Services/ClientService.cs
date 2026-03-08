using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using ToyDb.Extensions;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDbContracts.Data;
using ToyDb.Models;

namespace ToyDb.Services;

/// <summary>
/// Communicates with GRPC client
/// </summary>
public class ClientService(
    IReadStorageService readStorageService,
    IWriteStorageService writeStorageService,
    IWriteAheadLogRepository walRepository,
    IReplicationLogNotifier replicationLogNotifier,
    ILogger<ClientService> logger
) : Data.DataBase
{
    public override Task<KeyValueResponse> GetValue(GetRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(GetValue), request.Key);

        var value = readStorageService.GetValue(request.Key);
        var response = new KeyValueResponse()
        {
            Key = request.Key,
            Type = value.Type,
            Value = value.Data
        };

        timer.Stop();

        return Task.FromResult(response);
    }

    public override Task<GetAllValuesResponse> GetAllValues(GetAllValuesRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(GetAllValues));
        
        var values = readStorageService.GetValues();
        var keyValuePairs = values.Select(x => new KeyValueResponse()
        {
            Key = x.Key,
            Type = x.Value.Type,
            Value = x.Value.Data
        });

        var response = new GetAllValuesResponse();
        response.Values.AddRange(keyValuePairs);

        timer.Stop();

        return Task.FromResult(response);
    }

    public override async Task<KeyValueResponse> SetValue(KeyValueRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(SetValue), request.Key);

        await writeStorageService.SetValue(request.Key, new DatabaseEntry() {
            Timestamp = request.Timestamp,
            Key = request.Key,
            Type = request.Type,
            Data = request.Value
        });

        var storedEntry = readStorageService.GetValue(request.Key);

        timer.Stop();

        return new KeyValueResponse()
        {
            Key = storedEntry.Key,
            Type = storedEntry.Type,
            Value = storedEntry.Data
        };
    }

    public override async Task<DeleteResponse> DeleteValue(DeleteRequest request, ServerCallContext context)
    {
        var timer = logger.StartTimedLog(nameof(DeleteValue), request.Key);

        await writeStorageService.DeleteValue(request.Key);

        timer.Stop();

        return new DeleteResponse();
    }

    public override async Task StreamReplicationLog(StreamReplicationLogRequest request, IServerStreamWriter<ReplicationLogEntry> responseStream, ServerCallContext context)
    {
        try
        {
            var lastSentLsn = request.FromLsn - 1;

            var persistedEntries = walRepository.ReadFrom(request.FromLsn);
            foreach (var walEntry in persistedEntries)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    break;

                var replicationEntry = new ReplicationLogEntry
                {
                    Lsn = walEntry.Lsn,
                    Timestamp = walEntry.Timestamp,
                    Key = walEntry.Key,
                    Type = walEntry.Type,
                    Value = walEntry.Data ?? Google.Protobuf.ByteString.Empty,
                    IsDelete = walEntry.IsDelete
                };

                await responseStream.WriteAsync(replicationEntry);
                lastSentLsn = walEntry.Lsn;
            }

            await foreach (var walEntry in replicationLogNotifier.ReadAllAsync(context.CancellationToken))
            {
                // Skip entries that have already been sent
                if (walEntry.Lsn <= lastSentLsn)
                    continue;

                var replicationEntry = new ReplicationLogEntry
                {
                    Lsn = walEntry.Lsn,
                    Timestamp = walEntry.Timestamp,
                    Key = walEntry.Key,
                    Type = walEntry.Type,
                    Value = walEntry.Data ?? Google.Protobuf.ByteString.Empty,
                    IsDelete = walEntry.IsDelete
                };

                await responseStream.WriteAsync(replicationEntry);
                lastSentLsn = walEntry.Lsn;
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("StreamReplicationLog cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in StreamReplicationLog");
            throw;
        }
    }
}