namespace ToyDb.Services.SecondaryCatchUp;

public class SecondaryCatchUpOptions
{
    public const string Key = "SecondaryCatchUp";

    public int MaxRetryAttempts { get; set; } = 3;

    public int BaseRetryDelaySeconds { get; set; } = 1;

    public int ReconnectDelaySeconds { get; set; } = 2;
}
