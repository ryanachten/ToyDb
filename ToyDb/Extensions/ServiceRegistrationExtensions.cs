using ToyDb.Caches;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;
using ToyDb.Services.LogCompaction;

namespace ToyDb.Extensions;

public static class ServiceRegistrationExtensions
{
    public static void RegisterServiceCollection(this WebApplicationBuilder builder)
    {
        builder.RegisterCaches();

        builder.RegisterServices();

        builder.RegisterRepositories();
    }

    private static void RegisterCaches(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<KeyEntryOptions>(
            builder.Configuration.GetSection(KeyEntryOptions.Key));

        builder.Services.AddSingleton<IKeyOffsetCache, KeyOffsetCache>();

        builder.Services.AddSingleton<IKeyEntryCache, KeyEntryCache>();
    }

    private static void RegisterServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IDataStorageService, DataStorageService>();

        builder.Services.Configure<LogCompactionOptions>(
            builder.Configuration.GetSection(LogCompactionOptions.Key));

        builder.Services.AddHostedService<LogCompactionProcess>();
    }

    private static void RegisterRepositories(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<DataStoreOptions>(
            builder.Configuration.GetSection(DataStoreOptions.Key));

        builder.Services.AddScoped<IDataStoreRepository, DataStoreRepository>();

        builder.Services.Configure<WriteAheadLogOptions>(
            builder.Configuration.GetSection(WriteAheadLogOptions.Key));

        builder.Services.AddScoped<IWriteAheadLogRepository, WriteAheadLogRepository>();
    }
}
