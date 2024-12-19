using Microsoft.Extensions.Options;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;

namespace ToyDb.Extensions;

public static class ServiceRegistrationExtensions
{
    public static void RegisterServices(this WebApplicationBuilder builder)
    {
        builder.AddDataStorageRepository();
        builder.AddWriteAheadLogRepository();

        builder.Services.AddSingleton<IDataStorageService, DataStorageService>();
    }

    private static void AddDataStorageRepository(this WebApplicationBuilder builder)
    {
        var storeOptions = builder.Configuration.GetSection(DataStoreOptions.Key).Get<DataStoreOptions>();
        if (storeOptions == null) throw new InvalidOperationException("Missing 'DataStore' configuration");

        var storeRepository = new DataStoreRepository(Options.Create(storeOptions));

        builder.Services.AddSingleton<IDataStoreRepository, DataStoreRepository>((provider) => storeRepository)
            .AddOptions<WriteAheadLogOptions>().Bind(builder.Configuration.GetSection(DataStoreOptions.Key));
    }

    private static WriteAheadLogRepository AddWriteAheadLogRepository(this WebApplicationBuilder builder)
    {
        var walOptions = builder.Configuration.GetSection(WriteAheadLogOptions.Key).Get<WriteAheadLogOptions>();
        if (walOptions == null) throw new InvalidOperationException("Missing 'WriteAheadLog' configuration");

        var walRepository = new WriteAheadLogRepository(Options.Create(walOptions));

        builder.Services.AddSingleton<IWriteAheadLogRepository, WriteAheadLogRepository>((provider) => walRepository)
            .AddOptions<WriteAheadLogOptions>().Bind(builder.Configuration.GetSection(WriteAheadLogOptions.Key));

        return walRepository;
    }
}
