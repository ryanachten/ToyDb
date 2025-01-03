using Microsoft.Extensions.Options;

namespace ToyDb.Services.LogCompaction;

public sealed class LogCompactionProcess(
    IDataStorageService dataStorageService,
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

    // FIXME: we're still getting lock contention during writes
    private void CompactLogs(object? state)
    {
        dataStorageService.CompactLogs();
    }
}
