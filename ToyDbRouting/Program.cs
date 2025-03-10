using ToyDbRouting.Models;
using ToyDbRouting.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.Key));

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<RoutingService>();

await app.RunAsync();