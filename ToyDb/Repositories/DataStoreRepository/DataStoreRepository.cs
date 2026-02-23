using Microsoft.Extensions.Options;
using ToyDb.Models;

namespace ToyDb.Repositories.DataStoreRepository
{
    public class DataStoreRepository(
        IOptions<DataStoreOptions> options, ILogger<DataStoreRepository> logger
    ) : BaseLogRepository(logger, options.Value.Location), IDataStoreRepository
    {
        /// <summary>
        /// Reads a database entry from a given offset
        /// </summary>
        /// <param name="offset">Offset in log file to look for entry</param>
        /// <returns>Database entry based on log entry</returns>
        public DatabaseEntry GetValue(long offset)
        {
            using FileStream fileStream = new(logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Update stream pointer to offset before we attempt to read the entry
            fileStream.Seek(offset, SeekOrigin.Begin);
            using BinaryReader binaryReader = new(fileStream);

            var walEntry = ReadWalEntry(binaryReader);

            return new DatabaseEntry
            {
                Timestamp = walEntry.Timestamp,
                Key = walEntry.Key,
                Type = walEntry.Type,
                Data = walEntry.Data
            };
        }

        /// <summary>
        /// Reads all entries present in log file, only returning the latest values as database entries
        /// </summary>
        /// <returns>Dictionary with entry key assigned to tuple of entry and log offset</returns>
        public new Dictionary<string, (DatabaseEntry, long)> GetLatestEntries()
        {
            using FileStream fileStream = new(logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader binaryReader = new(fileStream);

            var entries = new Dictionary<string, (DatabaseEntry, long)>();

            while (fileStream.Position < fileStream.Length)
            {
                var offset = fileStream.Position;
                var walEntry = ReadWalEntry(binaryReader);

                if (walEntry.IsDelete)
                {
                    entries.Remove(walEntry.Key);
                    continue;
                }

                var entry = new DatabaseEntry
                {
                    Timestamp = walEntry.Timestamp,
                    Key = walEntry.Key,
                    Type = walEntry.Type,
                    Data = walEntry.Data
                };

                entries[walEntry.Key] = (entry, offset);
            }

            return entries;
        }

        /// <summary>
        /// Reads all entries present in log file, only returning the latest values as WAL entries with LSN metadata
        /// </summary>
        /// <returns>Dictionary with entry key assigned to tuple of WAL entry and log offset</returns>
        public Dictionary<string, (WalEntry, long)> GetLatestWalEntries()
        {
            using FileStream fileStream = new(logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader binaryReader = new(fileStream);

            var entries = new Dictionary<string, (WalEntry, long)>();

            while (fileStream.Position < fileStream.Length)
            {
                var offset = fileStream.Position;
                var walEntry = ReadWalEntry(binaryReader);

                if (walEntry.IsDelete)
                {
                    entries.Remove(walEntry.Key);
                    continue;
                }

                entries[walEntry.Key] = (walEntry, offset);
            }

            return entries;
        }

        /// <summary>
        /// Scans the data store to find the highest persisted LSN
        /// </summary>
        /// <returns>The highest LSN found in the data store, or 0 if empty</returns>
        public long GetLatestLsn()
        {
            using FileStream fileStream = new(logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader binaryReader = new(fileStream);

            long latestLsn = 0;

            while (fileStream.Position < fileStream.Length)
            {
                var walEntry = ReadWalEntry(binaryReader);
                if (walEntry.Lsn > latestLsn)
                    latestLsn = walEntry.Lsn;
            }

            return latestLsn;
        }

        /// <summary>
        /// Checks file for data redundancies, including duplicate keys and null values
        /// </summary>
        /// <returns>Whether there are data redundancies</returns>
        public bool HasRedundantData()
        {
            using FileStream fileStream = new(logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader binaryReader = new(fileStream);

            var keys = new HashSet<string>();

            while (fileStream.Position < fileStream.Length)
            {
                var walEntry = ReadWalEntry(binaryReader);

                if (walEntry.IsDelete || keys.Contains(walEntry.Key)) return true;

                keys.Add(walEntry.Key);
            }

            return false;
        }

        /// <summary>
        /// Appends a range of entities to the end of a log file
        /// </summary>
        /// <param name="entries">Entities to be added</param>
        /// <returns>Key - offset index</returns>
        public Dictionary<string, long> AppendRange(IEnumerable<WalEntry> entries)
        {
            // We need to lock the file while we operate on it to ensure concurrent writes don't break offset references or overwrite data
            using FileStream fileStream = new(logLocation, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            // Update stream pointer to end of the file and get offset
            fileStream.Seek(0, SeekOrigin.End);

            using BinaryWriter binaryWriter = new(fileStream);

            var index = new Dictionary<string, long>();

            foreach (var entry in entries)
            {
                var currentOffset = fileStream.Position;

                binaryWriter.Write(entry.Lsn);
                binaryWriter.Write(entry.Timestamp.ToDateTime().ToBinary());
                binaryWriter.Write(entry.Key);
                binaryWriter.Write(entry.Type.ToString());
                binaryWriter.Write(entry.Data?.ToBase64() ?? NullMarker.ToBase64());
                binaryWriter.Write(entry.IsDelete);

                index[entry.Key] = currentOffset;
            }

            return index;
        }
    }
}
