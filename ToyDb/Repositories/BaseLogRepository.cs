using Google.Protobuf;
using ToyDb.Messages;
using ToyDb.Models;

namespace ToyDb.Repositories;

/// <summary>
/// Base class for persisting logs to file
/// </summary>
public abstract class BaseLogRepository
{
    protected string logLocation;
    private readonly ILogger _logger;
    private readonly string _parentFolder;
    private string _logDirectory;

    protected BaseLogRepository(ILogger logger, string logLocation)
    {
        _logger = logger;
        _parentFolder = logLocation;

        GetLatestLogFilePath();
    }

    /// <summary>
    /// Appends database entry to log
    /// </summary>
    /// <param name="key">Key of the database entry</param>
    /// <param name="entry">Database entry</param>
    /// <returns>The current offset for the entry</returns>
    public long Append(string key, DatabaseEntry entry)
    {
        // We need to lock the file while we operate on it to ensure concurrent writes don't break offset references or overwrite data
        using FileStream fileStream = new(logLocation, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        // Update stream pointer to end of the file and get offset
        var currentOffset = fileStream.Seek(0, SeekOrigin.End);

        using BinaryWriter binaryWriter = new(fileStream);

        binaryWriter.Write(key);
        binaryWriter.Write(entry.Type.ToString());
        // TODO: remove Base64 encoding
        binaryWriter.Write(entry.Data?.ToBase64() ?? NullMarker.ToBase64());

        return currentOffset;
    }

    /// <summary>
    /// Reads all entries present in log file, only returning the latest values
    /// </summary>
    /// <returns>Dictionary with entry key assigned to tuple of entry and log offset</returns>
    public Dictionary<string, (DatabaseEntry, long)> GetLatestEntries()
    {
        using FileStream fileStream = new(logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader binaryReader = new(fileStream);

        var entries = new Dictionary<string, (DatabaseEntry, long)>();

        while (fileStream.Position < fileStream.Length)
        {
            var offset = fileStream.Position;
            var entry = ReadEntry(binaryReader);

            if (NullMarker.Equals(entry.Data))
            {
                entries.Remove(entry.Key);
                continue;
            }

            entries[entry.Key] = (entry, offset);
        }

        return entries;
    }

    public void CreateNewLogFile()
    {
        var fileName = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var filePath = Path.Combine(_logDirectory, fileName);

        Directory.CreateDirectory(_logDirectory);
        using FileStream fs = File.Create(filePath);

        logLocation = filePath;

        _logger.LogInformation("Created new log file at: {Location}", filePath);
    }

    protected static DatabaseEntry ReadEntry(BinaryReader binaryReader)
    {
        var key = binaryReader.ReadString();
        var rawDataType = binaryReader.ReadString();
        var data = binaryReader.ReadString();

        var hasValidDataType = Enum.TryParse(rawDataType, out DataType dataType);
        if (!hasValidDataType)
            throw new InvalidDataException($"Data type is not valid. Received {rawDataType}");

        return new DatabaseEntry() { Key = key, Type = dataType, Data = ByteString.FromBase64(data) };
    }

    private void GetLatestLogFilePath()
    {
        var nodeName = Environment.GetEnvironmentVariable("NODE_NAME") ?? string.Empty;
        var currentDirectory = Directory.GetCurrentDirectory();

        _logDirectory = Path.Combine(currentDirectory, _parentFolder, nodeName);

        if (!Directory.Exists(_logDirectory))
        {
            CreateNewLogFile();
            return;
        }

        var latestFilePath = Directory.EnumerateFiles(_logDirectory).Max();
        if (string.IsNullOrWhiteSpace(latestFilePath))
        {
            CreateNewLogFile();
            return;
        }

        logLocation = latestFilePath;
        
        _logger.LogInformation("Latest log file located at: {Location}", latestFilePath);
    }
}
