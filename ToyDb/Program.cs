using ToyDb.Repositories;
using ToyDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddSingleton<IDatabaseRepository, DatabaseRepository>();

var app = builder.Build();

app.MapGrpcService<DataService>();

await app.RunAsync();