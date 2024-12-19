using Microsoft.Extensions.Options;
using ToyDb.Repositories;
using ToyDb.Repositories.DataStoreRepository;
using ToyDb.Repositories.WriteAheadLogRepository;
using ToyDb.Services;

namespace ToyDb.Extensions;

public static class DataStorageServiceExtensions
{
    /// <summary>
    /// Registers the database storage service and all of its dependencies
    /// </summary>
    public static async Task AddDataStorageService(this WebApplicationBuilder builder)
    {
        /**
         * This whole process is complicated because we need to restore the index from a file during inititialization
         * which means we need to instantiate instances instead of just relying on DI
         */

        var walRepository = CreateWriteAheadLogRepository(builder);
        var dataStoreRepository = CreateDataStorageRepository(builder);

        var dataStorageService = new DataStorageService(dataStoreRepository, walRepository);

        await dataStorageService.RestoreIndexFromFile();

        builder.Services.AddSingleton<IDatabaseRepository, DataStorageService>((provider) => dataStorageService);
    }

    private static DataStoreRepository CreateDataStorageRepository(WebApplicationBuilder builder)
    {
        var storeOptions = builder.Configuration.GetSection(DataStoreOptions.Key).Get<DataStoreOptions>();
        if (storeOptions == null) throw new InvalidOperationException("Missing 'DataStore' configuration");

        var storeRepository = new DataStoreRepository(Options.Create(storeOptions));

        builder.Services.AddSingleton<IDataStoreRepository, DataStoreRepository>((provider) => storeRepository)
            .AddOptions<WriteAheadLogOptions>().Bind(builder.Configuration.GetSection(DataStoreOptions.Key));

        return storeRepository;
    }

    private static WriteAheadLogRepository CreateWriteAheadLogRepository(WebApplicationBuilder builder)
    {
        var walOptions = builder.Configuration.GetSection(WriteAheadLogOptions.Key).Get<WriteAheadLogOptions>();
        if (walOptions == null) throw new InvalidOperationException("Missing 'WriteAheadLog' configuration");

        var walRepository = new WriteAheadLogRepository(Options.Create(walOptions));

        builder.Services.AddSingleton<IWriteAheadLogRepository, WriteAheadLogRepository>((provider) => walRepository)
            .AddOptions<WriteAheadLogOptions>().Bind(builder.Configuration.GetSection(WriteAheadLogOptions.Key));

        return walRepository;
    }
}
