using Microsoft.Extensions.Options;

namespace ToyDb.Services.LogCompaction;

public sealed class LogCompactionProcess(
    IOptions<LogCompactionOptions> options,
    IDataStorageService dataStorageService) : IHostedService, IDisposable
{
    private Timer? _timer = null;

    public void Dispose()
    {
        _timer?.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(CompactLogs, null, TimeSpan.Zero, TimeSpan.FromSeconds(options.Value.CompactionInterval));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    private void CompactLogs(object? state) => dataStorageService.CompactLogs();
}
