namespace ToyDb.Services.LogCompaction;

public class LogCompactionOptions
{
    public static readonly string Key = "LogCompaction";

    /// <summary>
    /// Interval to run the compaction process in seconds
    /// </summary>
    public required int CompactionInterval { get; set; }
}
