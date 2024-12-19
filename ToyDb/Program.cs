using ToyDb.Extensions;
using ToyDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.RegisterServices();

var app = builder.Build();

app.MapGrpcService<ClientService>();

await app.RunAsync();