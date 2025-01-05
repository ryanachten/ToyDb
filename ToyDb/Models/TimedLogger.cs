using System.Diagnostics;

namespace ToyDb.Models;

public class TimedLogger<T>(ILogger<T> logger, string requestName)
{
    private readonly Stopwatch _stopwatch = new();
    private string? _key;

    public void Start(string? key)
    {
        _stopwatch.Start();
        _key = key;

        if(key != null)
        {
            logger.LogInformation(
                "Received request. RequestName: {RequestName}. Key: {Key}",
                requestName, key);
            return;
        }

        logger.LogInformation(
            "Received request. RequestName: {RequestName}",
            requestName);
    }

    public void Stop()
    {
        _stopwatch.Stop();

        if (_key != null)
        {
            logger.LogInformation(
                "Completed request. RequestName: {RequestName}. ElapsedTime: {ElapsedTime}. Key: {Key}",
                requestName, _stopwatch.ElapsedMilliseconds, _key);
            return;
        }

        logger.LogInformation(
            "Completed request. RequestName: {RequestName}. ElapsedTime: {ElapsedTime}",
            requestName, _stopwatch.ElapsedMilliseconds);
    }
}
