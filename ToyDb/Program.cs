using ToyDb.Repositories;
using ToyDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddSingleton<IDatabaseRepository, DatabaseRepository>();

var app = builder.Build();

app.MapGrpcService<GetterService>();
app.MapGrpcService<SetterService>();

app.Run();