using Microsoft.Extensions.Options;

namespace ToyDb.Repositories.WriteAheadLogRepository
{
    public class WriteAheadLogRepository(IOptions<WriteAheadLogOptions> options) : BaseLogRepository(options.Value.Location), IWriteAheadLogRepository
    {
    }
}
