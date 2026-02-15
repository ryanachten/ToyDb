using ToyDb.Extensions;
using ToyDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcHealthChecks();

builder.RegisterServiceCollection();

var app = builder.Build();

app.MapGrpcService<ClientService>();
app.MapGrpcHealthChecksService();

await app.RunAsync();