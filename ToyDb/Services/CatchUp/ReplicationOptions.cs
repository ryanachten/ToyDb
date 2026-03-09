namespace ToyDb.Services.CatchUp;

public class ReplicationOptions
{
    public const string Key = "Replication";
    public string? PrimaryNodeAddress { get; set; }
}
