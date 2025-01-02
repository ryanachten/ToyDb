using Microsoft.Extensions.Options;
using ToyDb.Models;

namespace ToyDb.Repositories.DataStoreRepository
{
    public class DataStoreRepository(IOptions<DataStoreOptions> options) : BaseLogRepository(options.Value.Location), IDataStoreRepository
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

            return ReadEntry(binaryReader);
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
                var entry = ReadEntry(binaryReader);
                
                if (keys.Contains(entry.Key) || NullMarker.Equals(entry.Data)) return true;

                keys.Add(entry.Key);
            }

            return false;
        }

        /// <summary>
        /// Appends a range of entities to the end of a log file
        /// </summary>
        /// <param name="entries">Entities to be added</param>
        /// <returns>Key - offset index</returns>
        public Dictionary<string, long> AppendRange(IEnumerable<DatabaseEntry> entries)
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

                binaryWriter.Write(entry.Key);
                binaryWriter.Write(entry.Type.ToString());
                binaryWriter.Write(entry.Data.ToBase64());

                index.Add(entry.Key, currentOffset);
            }

            return index;
        }
    }
}
