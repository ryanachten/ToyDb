namespace ToyDb.Repositories.ReplicationLogRepository;

public class ReplicationLogOptions
{
    public const string Key = "ReplicationLog";
    public string LogLocation { get; set; } = "bin/replication-log";
}
