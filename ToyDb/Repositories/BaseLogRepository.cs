using Google.Protobuf;
using ToyDb.Messages;
using ToyDb.Models;

namespace ToyDb.Repositories;

/// <summary>
/// Base class for persisting logs to file
/// </summary>
public abstract class BaseLogRepository
{
    private readonly char _logDelimiter = ':';
    private readonly int _expectedLogPartCount = 3;
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
    public async Task<long> Append(string key, DatabaseEntry entry, CancellationToken cancellationToken)
    {
        var logEntry = $"{key}{_logDelimiter}{entry.Type}{_logDelimiter}{entry.Data.ToBase64()}";

        // We need to lock the file while we operate on it to ensure concurrent writes don't break offset references or overwrite data
        using FileStream fileStream = new(_logLocation, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        // Update stream pointer to end pf the file and get offset
        var currentOffset = fileStream.Seek(0, SeekOrigin.End);

        using StreamWriter streamWriter = new(fileStream);

        await streamWriter.WriteLineAsync(logEntry);

        return currentOffset;
    }

    /// <summary>
    /// Reads a database entry from a given offset
    /// </summary>
    /// <param name="offset">Offset in log file to look for entry</param>
    /// <param name="cancellationToken">Token to cancel request</param>
    /// <returns>Database entry based on log entry</returns>
    public async Task<DatabaseEntry> Read(long offset, CancellationToken cancellationToken)
    {
        using FileStream fileStream = new(_logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Update stream pointer to offset before we attempt to read the entry
        fileStream.Seek(offset, SeekOrigin.Begin);

        using StreamReader streamReader = new(fileStream);

        var logLine = await streamReader.ReadLineAsync(cancellationToken);

        return ParseLogLine(logLine, offset);
    }

    /// <summary>
    /// Reads all entries present in log file
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Dictionary with entry key assigned to tuple of entry and log offset</returns>
    public async Task<Dictionary<string, (DatabaseEntry, long)>> ReadAll(CancellationToken cancellationToken)
    {
        using FileStream fileStream = new(_logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
        using StreamReader streamReader = new(fileStream);

        var entries = new Dictionary<string, (DatabaseEntry, long)>();
        string? line;
        long currentOffset = 0;
        while ((line = await streamReader.ReadLineAsync(cancellationToken)) != null)
        {
            var entry = ParseLogLine(line, currentOffset);
            entries.Add(entry.Key, (entry, currentOffset));
            currentOffset += line.Length;
        }

        return entries;
    }

    private DatabaseEntry ParseLogLine(string? logLine, long offset)
    {
        if (string.IsNullOrWhiteSpace(logLine))
            throw new InvalidDataException($"No log entry found for offset {offset}");

        var logParts = logLine.Split(_logDelimiter);
        if (logParts.Length < _expectedLogPartCount)
            throw new InvalidDataException($"Incorrect number of logs parts when reading offset {offset}. Expected {_expectedLogPartCount}, received {logParts.Length}");

        var key = logParts[0];
        var rawDataType = logParts[1];
        var data = logParts[2];

        var hasValidDataType = Enum.TryParse(rawDataType, out DataType dataType);
        if (!hasValidDataType)
            throw new InvalidDataException($"Data type is not valid. Received {rawDataType}");

        return new DatabaseEntry() { Key = key, Type = dataType, Data = ByteString.FromBase64(data) };
    }

    private void EnsureLogFileIsPresent()
    {
        if (!File.Exists(_logLocation))
        {
            using FileStream fs = File.Create(_logLocation);
        }
    }
}
