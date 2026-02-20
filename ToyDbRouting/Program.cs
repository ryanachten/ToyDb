using ToyDbRouting.Models;
using ToyDbRouting.Services;
using ToyDbRouting.Utilities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.Key));
builder.Services.AddSingleton<INtpService, NtpService>();

builder.Services.AddGrpc();
builder.Services.AddAutoMapper(typeof(AutoMapperProfile));
builder.Services.AddSingleton<HealthProbeService>();

builder.Services.AddHostedService(provider => provider.GetRequiredService<HealthProbeService>());

var app = builder.Build();

app.MapGrpcService<RoutingService>();

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