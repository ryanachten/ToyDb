using Microsoft.Extensions.Options;
using ToyDbRouting.Models;
using ToyDbRouting.Services;
using ToyDbRouting.Utilities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.Key));
builder.Services.AddSingleton<INtpService, NtpService>();
builder.Services.AddSingleton<ConsistentHashRing>();

builder.Services.AddGrpc();
builder.Services.AddAutoMapper(typeof(AutoMapperProfile));
builder.Services.AddSingleton<HealthProbeService>();
builder.Services.AddSingleton<DeadLetterQueueService>();
builder.Services.AddSingleton<PartitionManager>();

builder.Services.AddHostedService(provider => provider.GetRequiredService<HealthProbeService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<DeadLetterQueueService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<PartitionManager>());

var app = builder.Build();

app.MapGrpcService<RoutingService>();

app.MapGet("/health", (IOptions<RoutingOptions> routingOptions) =>
{
    return Results.Ok(new
    {
        Status = "Healthy",
        InstanceId = routingOptions.Value.RouterInstanceId,
        Timestamp = DateTime.UtcNow
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapGet("/diagnostics/health", (HealthProbeService healthProbeService) =>
    {
        var statuses = healthProbeService.HealthStates
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

        return Results.Ok(statuses);
    });
}

await app.RunAsync();