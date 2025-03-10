namespace ToyDbRouting.Extensions;

public static class TaskExtensions
{
    /// <summary>
    /// Executes tasks in parallel for a given number of tasks to complete before returning
    /// </summary>
    /// <param name="tasks">Tasks to run in parallel</param>
    /// <param name="threshold">Threshold of tasks to wait to complete before returning</param>
    /// <returns>Task to await for threshold to be met</returns>
    public static async Task WhenThresholdCompleted(this IEnumerable<Task> tasks, int threshold)
    {
        if (threshold == 0)
        {
            tasks.FireAndForget();
            return;
        }

        var currentCompletedTasks = 0;
        await foreach (var task in Task.WhenEach(tasks))
        {
            // TODO: handle failure cases
            await task;

            currentCompletedTasks++;

            // If we've met the threshold, then exit
            if (currentCompletedTasks >= threshold) return;
        }
    }

    /// <summary>
    /// Executes tasks but does not wait for the results
    /// </summary>
    /// <param name="tasks">Tasks to be executed</param>
    public static void FireAndForget(this IEnumerable<Task> tasks)
    {
        foreach (var task in tasks)
        {
            _ = Task.Run(async () =>
            {
                // TODO: handle failure cases
                await task;
            });
        }
    }
}
