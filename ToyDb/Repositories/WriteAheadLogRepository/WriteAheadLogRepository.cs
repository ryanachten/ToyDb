using Microsoft.Extensions.Options;

namespace ToyDb.Repositories.WriteAheadLogRepository
{
    public class WriteAheadLogRepository(
        ILogger<WriteAheadLogRepository> logger, IOptions<WriteAheadLogOptions> options
    ) : BaseLogRepository(logger, options.Value.Location), IWriteAheadLogRepository
    {
    }
}
