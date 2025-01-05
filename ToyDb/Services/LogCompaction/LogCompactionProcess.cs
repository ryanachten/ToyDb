using ToyDb.Extensions;

namespace ToyDb.Services.LogCompaction;

public class LogCompactionProcess(
    IWriteStorageService dataStorageService, 
    ILogger<LogCompactionProcess> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = logger.StartTimedLog(nameof(ExecuteAsync));

        await dataStorageService.CompactLogs();

        timer.Stop();
    }
}
