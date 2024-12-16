using ToyDb.Repositories;
using ToyDb.Services;
using ToyDb.Services.AppendOnlyLogService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddSingleton<IDatabaseRepository, DatabaseFileRepository>();
builder.Services.AddSingleton<IAppendOnlyLogService, AppendOnlyLogService>()
    .AddOptions<AppendOnlyLogOptions>().Bind(builder.Configuration.GetSection(AppendOnlyLogOptions.Key));

var app = builder.Build();

app.MapGrpcService<ClientService>();

await app.RunAsync();