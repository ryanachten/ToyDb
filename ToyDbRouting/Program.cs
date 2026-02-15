using ToyDbRouting.Models;
using ToyDbRouting.Services;
using ToyDbRouting.Utilities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.Key));
builder.Services.AddSingleton<INtpService, NtpService>();

builder.Services.AddGrpc();
builder.Services.AddHostedService<HealthProbeService>();
builder.Services.AddAutoMapper(typeof(AutoMapperProfile));

var app = builder.Build();

app.MapGrpcService<RoutingService>();

await app.RunAsync();