using Microsoft.Extensions.Diagnostics.HealthChecks;
using ToyDb.Extensions;
using ToyDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 500 * 1024 * 1024; // 500 MB
    options.MaxSendMessageSize = 500 * 1024 * 1024;
});
builder.Services.AddGrpcHealthChecks()
    // TODO: should this do something more meaningful
    .AddCheck("ping", () => HealthCheckResult.Healthy());

builder.Services.AddSingleton<ReplicaState>();

builder.RegisterServiceCollection();

var app = builder.Build();

app.MapGrpcService<ClientService>();
app.MapGrpcService<ClusterService>();
app.MapGrpcHealthChecksService();

await app.RunAsync();