using Microsoft.Extensions.Options;
using ToyDb.Models;

namespace ToyDb.Repositories.WriteAheadLogRepository
{
    public class WriteAheadLogRepository(
        ILogger<WriteAheadLogRepository> logger, IOptions<WriteAheadLogOptions> options
    ) : BaseLogRepository(logger, options.Value.Location), IWriteAheadLogRepository
    {
        public IEnumerable<WalEntry> ReadFrom(long fromLsn)
        {
            using FileStream fileStream = new(logLocation, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
            using BinaryReader binaryReader = new(fileStream);

            while (fileStream.Position < fileStream.Length)
            {
                var entry = ReadWalEntry(binaryReader);

                if (entry.Lsn >= fromLsn)
                    yield return entry;
            }
        }

        public long GetLatestLsn()
        {
            if (!File.Exists(logLocation))
                return 0;

            using FileStream fileStream = new(logLocation, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader binaryReader = new(fileStream);

            long latestLsn = 0;

            while (fileStream.Position < fileStream.Length)
            {
                var entry = ReadWalEntry(binaryReader);
                latestLsn = entry.Lsn;
            }

            return latestLsn;
        }
    }
}
