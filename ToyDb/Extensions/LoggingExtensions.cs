using ToyDb.Models;

namespace ToyDb.Extensions;

public static class LoggingExtensions
{
    public static TimedLogger<T> StartTimedLog<T>(this ILogger<T> logger, string requestName, string? key = null)
    {
        var timer = new TimedLogger<T>(logger, requestName);
        
        timer.Start(key);

        return timer;
    }
}
