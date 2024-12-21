using Microsoft.Extensions.Options;

namespace ToyDb.Services.LogCompaction;

public sealed class LogCompactionProcess(
    IServiceProvider servicesProvider,
    IOptions<LogCompactionOptions> options) : IHostedService, IDisposable
{
    private Timer? _timer = null;

    public void Dispose()
    {
        _timer?.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(CompactLogs, null, TimeSpan.FromSeconds(options.Value.CompactionInterval), TimeSpan.FromSeconds(options.Value.CompactionInterval));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    private void CompactLogs(object? state)
    {
        using var scope = servicesProvider.CreateScope();

        var dataStorageService = scope.ServiceProvider.GetRequiredService<IDataStorageService>();

        dataStorageService.CompactLogs();
    }
}
