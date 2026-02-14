using ToyDbRouting.Models;

namespace ToyDbRouting.Extensions;

public static class RetryHelper
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        RetryOptions options,
        ILogger logger,
        string operationName)
    {
        int attempt = 0;
        int delayMs = options.BaseDelayMs;

        while (true)
        {
            try
            {
                attempt++;
                var result = await operation();

                if (attempt > 1)
                {
                    logger.LogInformation(
                        "Operation {OperationName} succeeded on attempt {Attempt} after {Retries} retries",
                        operationName, attempt, attempt - 1);
                }

                return result;
            }
            catch (Exception ex)
            {
                if (attempt > options.MaxRetries)
                {
                    logger.LogWarning(
                        "Operation {OperationName} failed after {Attempts} attempts: {Error}",
                        operationName, attempt, ex.Message);
                    throw;
                }

                logger.LogWarning(
                    "Operation {OperationName} attempt {Attempt}/{MaxRetries} failed: {Error}. Retrying in {Delay}ms...",
                    operationName, attempt, options.MaxRetries + 1, ex.Message, delayMs);

                await Task.Delay(delayMs);
                delayMs = Math.Min(delayMs * 2, options.MaxDelayMs);
            }
        }
    }

    public static Task ExecuteWithRetryAsync(
        Func<Task> operation,
        RetryOptions options,
        ILogger logger,
        string operationName)
    {
        return ExecuteWithRetryAsync(
            async () =>
            {
                await operation();
                return 0;
            },
            options,
            logger,
            operationName);
    }
}
