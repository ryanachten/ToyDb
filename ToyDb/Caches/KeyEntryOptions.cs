namespace ToyDb.Caches;

public class KeyEntryOptions
{
    public static readonly string Key = "KeyEntryCache";

    /// <summary>
    /// Sliding window in seconds for how long an entry will be retained in cache
    /// since it was last accessed
    /// </summary>
    public required int SlidingRetentionPeriod { get; set; }
}
