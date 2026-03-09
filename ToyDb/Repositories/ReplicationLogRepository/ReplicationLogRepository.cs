using Google.Protobuf;
using Microsoft.Extensions.Options;
using ToyDb.Models;
using ToyDbContracts.Data;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace ToyDb.Repositories.ReplicationLogRepository;

// TODO: directory resolution and ReadEntry deserialization are duplicated from BaseLogRepository.
// Resolve by extracting a thinner base class that owns only file location + entry serialization,
// with the rotating file logic moved to a subclass. ReplicationLogRepository can then extend the thin base.
public class ReplicationLogRepository : IReplicationLogRepository
{
    private readonly string _logLocation;
    private long _nextLsn;
    private readonly ILogger<ReplicationLogRepository> _logger;

    public ReplicationLogRepository(ILogger<ReplicationLogRepository> logger, IOptions<ReplicationLogOptions> options)
    {
        _logger = logger;

        var nodeName = Environment.GetEnvironmentVariable("NODE_NAME") ?? string.Empty;
        var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), options.Value.LogLocation, nodeName);

        Directory.CreateDirectory(logDirectory);

        _logLocation = Path.Combine(logDirectory, "replication.log");

        _nextLsn = ScanForLastLsn() + 1;

        _logger.LogInformation("Replication log initialised at {Location}, next LSN: {Lsn}", _logLocation, _nextLsn);
    }

    public long Append(string key, DatabaseEntry entry)
    {
        var lsn = Interlocked.Increment(ref _nextLsn) - 1;

        using FileStream fileStream = new(_logLocation, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        fileStream.Seek(0, SeekOrigin.End);
        using BinaryWriter writer = new(fileStream);

        writer.Write(lsn);
        writer.Write(entry.Timestamp.ToDateTime().ToBinary());
        writer.Write(key);
        writer.Write(entry.Type.ToString());
        writer.Write(entry.Data?.ToBase64() ?? NullMarker.ToBase64());

        return lsn;
    }

    public long GetLatestLsn() => _nextLsn - 1;

    public IEnumerable<ReplicationLogEntry> GetEntriesFromLsn(long fromLsn)
    {
        if (!File.Exists(_logLocation))
            yield break;

        using FileStream fileStream = new(_logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(fileStream);

        while (fileStream.Position < fileStream.Length)
        {
            var lsn = reader.ReadInt64();
            var rawTimestamp = reader.ReadInt64();
            var key = reader.ReadString();
            var rawDataType = reader.ReadString();
            var data = reader.ReadString();

            if (lsn < fromLsn)
                continue;

            if (!Enum.TryParse(rawDataType, out DataType dataType))
                throw new InvalidDataException($"Invalid data type in replication log: {rawDataType}");

            var entry = new DatabaseEntry
            {
                Timestamp = Timestamp.FromDateTime(DateTime.FromBinary(rawTimestamp)),
                Key = key,
                Type = dataType,
                Data = ByteString.FromBase64(data)
            };

            yield return new ReplicationLogEntry(lsn, entry);
        }
    }

    private long ScanForLastLsn()
    {
        if (!File.Exists(_logLocation))
            return -1;

        using FileStream fileStream = new(_logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(fileStream);

        if (fileStream.Length == 0)
            return -1;

        long lastLsn = 0;

        while (fileStream.Position < fileStream.Length)
        {
            lastLsn = reader.ReadInt64();
            reader.ReadInt64();
            reader.ReadString();
            reader.ReadString();
            reader.ReadString();
        }

        return lastLsn;
    }
}
