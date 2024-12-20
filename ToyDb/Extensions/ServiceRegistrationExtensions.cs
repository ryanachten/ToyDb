using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;
using ToyDb.Services.LogCompaction;

namespace ToyDb.Extensions;

public static class ServiceRegistrationExtensions
{
    public static void RegisterServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<DataStoreOptions>(
            builder.Configuration.GetSection(DataStoreOptions.Key));

        builder.Services.AddSingleton<IDataStoreRepository, DataStoreRepository>();

        builder.Services.Configure<WriteAheadLogOptions>(
            builder.Configuration.GetSection(WriteAheadLogOptions.Key));

        builder.Services.AddSingleton<IWriteAheadLogRepository, WriteAheadLogRepository>();

        // TODO: we need to move the index into a cache to avoid all these having to be singletons
        builder.Services.AddSingleton<IDataStorageService, DataStorageService>();

        builder.Services.Configure<LogCompactionOptions>(
            builder.Configuration.GetSection(LogCompactionOptions.Key));

        builder.Services.AddHostedService<LogCompactionProcess>();
    }
}
