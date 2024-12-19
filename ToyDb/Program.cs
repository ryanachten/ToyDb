using Microsoft.Extensions.Options;
using ToyDb.Repositories;
using ToyDb.Services;
using ToyDb.Services.AppendOnlyLogService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var aolServiceOptions = builder.Configuration.GetSection(AppendOnlyLogOptions.Key).Get<AppendOnlyLogOptions>();
if (aolServiceOptions == null) throw new InvalidOperationException("Missing 'AppendOnlyLog' configuration");

var aolService = new AppendOnlyLogService(Options.Create(aolServiceOptions));

var dbFileRepository = new DatabaseFileRepository(aolService);
await dbFileRepository.RestoreIndexFromFile();

builder.Services.AddSingleton<IAppendOnlyLogService, AppendOnlyLogService>((provider) => aolService)
    .AddOptions<AppendOnlyLogOptions>().Bind(builder.Configuration.GetSection(AppendOnlyLogOptions.Key));
builder.Services.AddSingleton<IDatabaseRepository, DatabaseFileRepository>((provider) => dbFileRepository);

var app = builder.Build();

app.MapGrpcService<ClientService>();

await app.RunAsync();