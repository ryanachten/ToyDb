namespace ToyDb.Repositories.WriteAheadLogRepository;

public class WriteAheadLogOptions
{
    public static readonly string Key = "WriteAheadLog";
    public required string Location { get; set; }
}
