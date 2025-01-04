namespace ToyDb.Services.LogCompaction;

public class LogCompactionProcess(IWriteStorageService dataStorageService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await dataStorageService.CompactLogs();
    }
}
