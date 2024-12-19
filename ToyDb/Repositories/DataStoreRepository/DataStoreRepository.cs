using Microsoft.Extensions.Options;

namespace ToyDb.Repositories.DataStoreRepository
{
    public class DataStoreRepository(IOptions<DataStoreOptions> options) : BaseLogRepository(options.Value.Location), IDataStoreRepository
    {
    }
}
