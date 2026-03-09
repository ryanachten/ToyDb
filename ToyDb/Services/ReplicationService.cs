using Grpc.Core;
using ToyDb.Repositories.ReplicationLogRepository;
using ToyDbContracts.Data;

namespace ToyDb.Services;

public class ReplicationService(IReplicationLogRepository replicationLogRepository) : Data.DataBase
{
    public override async Task StreamReplicationLog(
        StreamReplicationLogRequest request,
        IServerStreamWriter<ReplicationLogEntryMessage> responseStream,
        ServerCallContext context)
    {
        foreach (var entry in replicationLogRepository.GetEntriesFromLsn(request.FromLsn))
        {
            await responseStream.WriteAsync(new ReplicationLogEntryMessage
            {
                Lsn = entry.Lsn,
                Timestamp = entry.Entry.Timestamp,
                Type = entry.Entry.Type,
                Key = entry.Entry.Key,
                Value = entry.Entry.Data
            });
        }
    }
}
