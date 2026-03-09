using Microsoft.Extensions.Diagnostics.HealthChecks;
using ToyDb.Extensions;
using ToyDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcHealthChecks()
    // TODO: should this do something more meaningful
    .AddCheck("ping", () => HealthCheckResult.Healthy());

builder.RegisterServiceCollection();

var app = builder.Build();

app.MapGrpcService<ClientService>();
app.MapGrpcHealthChecksService();

await app.RunAsync();