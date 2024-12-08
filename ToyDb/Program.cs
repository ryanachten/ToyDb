using ToyDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<GetterService>();
app.MapGrpcService<SetterService>();

app.Run();