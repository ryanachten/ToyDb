namespace ToyDb.Models;

public record ReplicationLogEntry(long Lsn, DatabaseEntry Entry);
