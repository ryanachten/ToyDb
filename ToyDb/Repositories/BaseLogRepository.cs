using Google.Protobuf;
using ToyDb.Messages;
using ToyDb.Models;

namespace ToyDb.Repositories;

/// <summary>
/// Base class for persisting logs to file
/// </summary>
public abstract class BaseLogRepository
{
    private readonly string _logLocation;

    protected BaseLogRepository(string logLocation)
    {
        _logLocation = logLocation;
        EnsureLogFileIsPresent();
    }

    /// <summary>
    /// Appends database entry to log
    /// </summary>
    /// <param name="key">Key of the database entry</param>
    /// <param name="entry">Database entry</param>
    /// <param name="cancellationToken">Token to cancel request</param>
    /// <returns>The current offset for the entry</returns>
    public long Append(string key, DatabaseEntry entry, CancellationToken cancellationToken)
    {
        // We need to lock the file while we operate on it to ensure concurrent writes don't break offset references or overwrite data
        using FileStream fileStream = new(_logLocation, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        // Update stream pointer to end pf the file and get offset
        var currentOffset = fileStream.Seek(0, SeekOrigin.End);

        using BinaryWriter binaryWriter = new(fileStream);

        binaryWriter.Write(key);
        binaryWriter.Write(entry.Type.ToString());
        binaryWriter.Write(entry.Data.ToBase64());

        return currentOffset;
    }

    /// <summary>
    /// Reads a database entry from a given offset
    /// </summary>
    /// <param name="offset">Offset in log file to look for entry</param>
    /// <param name="cancellationToken">Token to cancel request</param>
    /// <returns>Database entry based on log entry</returns>
    public DatabaseEntry Read(long offset, CancellationToken cancellationToken)
    {
        using FileStream fileStream = new(_logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Update stream pointer to offset before we attempt to read the entry
        fileStream.Seek(offset, SeekOrigin.Begin);

        using BinaryReader binaryReader = new(fileStream);

        return ReadEntry(binaryReader);
    }

    /// <summary>
    /// Reads all entries present in log file
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Dictionary with entry key assigned to tuple of entry and log offset</returns>
    public Dictionary<string, (DatabaseEntry, long)> ReadAll(CancellationToken cancellationToken)
    {
        using FileStream fileStream = new(_logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader binaryReader = new(fileStream);

        var entries = new Dictionary<string, (DatabaseEntry, long)>();

        while (fileStream.Position < fileStream.Length)
        {
            var offset = fileStream.Position;
            var entry = ReadEntry(binaryReader);

            entries[entry.Key] = (entry, offset);
        }

        return entries;
    }

    private void EnsureLogFileIsPresent()
    {
        if (!File.Exists(_logLocation))
        {
            using FileStream fs = File.Create(_logLocation);
        }
    }

    private static DatabaseEntry ReadEntry(BinaryReader binaryReader)
    {
        var key = binaryReader.ReadString();
        var rawDataType = binaryReader.ReadString();
        var data = binaryReader.ReadString();

        var hasValidDataType = Enum.TryParse(rawDataType, out DataType dataType);
        if (!hasValidDataType)
            throw new InvalidDataException($"Data type is not valid. Received {rawDataType}");

        return new DatabaseEntry() { Key = key, Type = dataType, Data = ByteString.FromBase64(data) };
    }
}
