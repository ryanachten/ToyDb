namespace ToyDb.Services.AppendOnlyLogService;

public class AppendOnlyLogOptions
{
    public readonly static string Key = "AppendOnlyLog";

    public required string Location { get; set; }
}
