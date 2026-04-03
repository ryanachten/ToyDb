using ToyDb.Caches;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;
using ToyDb.Services.LogCompaction;
using ToyDb.Services.SecondaryCatchUp;

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
        builder.Services.Configure<ReplicaOptions>(
            builder.Configuration.GetSection(ReplicaOptions.Key));

        builder.Services.Configure<ClusterOptions>(
            builder.Configuration.GetSection(ClusterOptions.Key));

        builder.Services.AddSingleton<ILsnProvider, LsnProvider>();

        builder.Services.AddSingleton<IReplicationLogNotifier, ReplicationLogNotifier>();

        builder.Services.AddSingleton<WalRecoveryService>();

        builder.Services.AddSingleton<IReadStorageService, ReadStorageService>();

        builder.Services.AddSingleton<IWriteStorageService, WriteStorageService>();

        builder.Services.Configure<LogCompactionOptions>(
            builder.Configuration.GetSection(LogCompactionOptions.Key));

        builder.Services.Configure<SecondaryCatchUpOptions>(
            builder.Configuration.GetSection(SecondaryCatchUpOptions.Key));

        builder.Services.AddHostedService<SecondaryCatchUpService>();

        builder.Services.AddHostedService<ElectionService>();

        builder.Services.AddHostedService<LogCompactionProcess>();
    }

    private static void RegisterRepositories(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<DataStoreOptions>(
            builder.Configuration.GetSection(DataStoreOptions.Key));

        builder.Services.AddSingleton<IDataStoreRepository, DataStoreRepository>();

        builder.Services.Configure<WriteAheadLogOptions>(
            builder.Configuration.GetSection(WriteAheadLogOptions.Key));

        builder.Services.AddSingleton<IWriteAheadLogRepository, WriteAheadLogRepository>();
    }
}
