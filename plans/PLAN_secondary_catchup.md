# Plan: Secondary Catch-Up on Startup

> **Date:** March 9, 2026
> **Addresses:** REPLICATION_REVIEW.md § 7, Phase 2, Point 5
> **Branch:** `feature/add-replication-log`

---

## Problem

If a secondary replica is offline when a write arrives, that write is permanently lost for that replica. There is no mechanism for the secondary to detect the gap or recover missing entries when it comes back online. This means secondaries silently diverge from the primary after any outage.

---

## Design Overview

The catch-up mechanism requires three new capabilities:

1. **Replication log on every node** — A separate, append-only log that records every write in strict LSN (Log Sequence Number) order. Unlike the WAL (which records file byte offsets) or the DataStore (which compacts), the replication log uses a stable, ever-increasing int64 sequence number that survives restarts.

2. **A streaming gRPC endpoint** — Each node exposes `StreamReplicationLog(fromLsn)` that streams all replication log entries with LSN ≥ `fromLsn`. Secondaries call this on the primary.

3. **A secondary catch-up service** — A hosted service that runs at startup. If the node is configured as a secondary (via `PRIMARY_NODE_ADDRESS`), it connects to the primary's streaming endpoint, pulls all entries since its last known LSN, and replays them locally.

---

## Architecture

```
Secondary startup
       │
       ▼
SecondaryCatchUpService
  1. Read own latest LSN from ReplicationLogRepository
  2. Connect to PRIMARY_NODE_ADDRESS
  3. Call StreamReplicationLog(fromLsn: myLastLsn + 1)
  4. For each streamed ReplicationLogEntry:
       → WriteStorageService.SetValue() or DeleteValue()
       → (WriteStorageService also appends to local ReplicationLog)
  5. Stream ends → node is caught up
```

```
Write path (unchanged externally)
       │
       ▼
WriteStorageService.ExecuteSetValue / ExecuteDeleteValue
  → WAL Append
  → DataStore Append
  → ReplicationLogRepository.Append   ← NEW
  → Cache updates
```

---

## New Components

### 1. `ReplicationLogEntry` (model)

**File:** `ToyDb/Models/ReplicationLogEntry.cs`

```csharp
public record ReplicationLogEntry(long Lsn, DatabaseEntry Entry);
```

---

### 2. `IReplicationLogRepository` + `ReplicationLogRepository`

**Files:** `ToyDb/Repositories/ReplicationLogRepository/`

Interface:
```csharp
public interface IReplicationLogRepository
{
    long Append(string key, DatabaseEntry entry);
    long GetLatestLsn();
    IEnumerable<ReplicationLogEntry> GetEntriesFromLsn(long fromLsn);
}
```

Implementation notes:
- Does **not** extend `BaseLogRepository` — different read semantics (ordered replay, not latest-per-key deduplication).
- Binary format per record: `[lsn: int64][timestamp: int64][key: string][dataType: string][data: base64]`
- LSN counter: an in-memory `long _nextLsn` field, initialized on construction by scanning the file to find the last written LSN (or 0 if empty). Uses `Interlocked.Increment` for thread safety.
- Storage path: `bin/replication-log/{NODE_NAME}/` — consistent with the existing WAL/DataStore convention.
- `GetEntriesFromLsn(fromLsn)`: linear scan of the file, yielding entries where `lsn >= fromLsn`. (Binary search optimisation can be added later.)
- The replication log is **never compacted** — it is the source of truth for catch-up. Pruning entries that all secondaries have consumed is a future concern.

Config class:
```csharp
public class ReplicationLogOptions
{
    public const string Key = "ReplicationLog";
    public string LogLocation { get; set; } = "bin/replication-log";
}
```

---

### 3. `data.proto` — New RPC

**File:** `ToyDbContracts/Protos/data.proto`

Add to the `Data` service:

```proto
rpc StreamReplicationLog (StreamReplicationLogRequest) returns (stream ReplicationLogEntryMessage);
```

New messages:

```proto
message StreamReplicationLogRequest {
  int64 from_lsn = 1;
}

message ReplicationLogEntryMessage {
  int64 lsn = 1;
  google.protobuf.Timestamp timestamp = 2;
  DataType type = 3;
  string key = 4;
  google.protobuf.BytesValue value = 5;
}
```

Server-streaming is used so large catch-ups don't require holding all entries in memory on either side.

---

### 4. `ReplicationService` (gRPC handler)

**File:** `ToyDb/Services/ReplicationService.cs`

A new gRPC service class (registered in `Program.cs` via `app.MapGrpcService<ReplicationService>()`):

```csharp
public class ReplicationService : Data.DataBase   // or a separate generated base
{
    public override async Task StreamReplicationLog(
        StreamReplicationLogRequest request,
        IServerStreamWriter<ReplicationLogEntryMessage> responseStream,
        ServerCallContext context)
    {
        foreach (var entry in _replicationLogRepository.GetEntriesFromLsn(request.FromLsn))
        {
            await responseStream.WriteAsync(MapToMessage(entry));
        }
    }
}
```

Alternatively, this can be added to the existing `ClientService` if keeping all Data RPC handlers together is preferred. A separate class avoids polluting `ClientService` with replication concerns.

---

### 5. `WriteStorageService` changes

**File:** `ToyDb/Services/WriteStorageService.cs`

- Inject `IReplicationLogRepository` via constructor.
- In `ExecuteSetValue`: call `_replicationLogRepository.Append(key, value)` after the existing WAL and DataStore appends.
- In `ExecuteDeleteValue`: call `_replicationLogRepository.Append(key, DatabaseEntry.Null(key))` similarly.
- Compaction (`ExecuteCompactLogs`) is unchanged — it does not touch the replication log.

---

### 6. `ReplicationOptions` (config)

**File:** `ToyDb/Services/CatchUp/ReplicationOptions.cs`

```csharp
public class ReplicationOptions
{
    public const string Key = "Replication";

    /// <summary>
    /// Address of this node's primary replica.
    /// If null or empty, this node is a primary and catch-up is skipped.
    /// Example: "https://toydb-p1-r1:8081"
    /// </summary>
    public string? PrimaryNodeAddress { get; set; }
}
```

---

### 7. `SecondaryCatchUpService`

**File:** `ToyDb/Services/CatchUp/SecondaryCatchUpService.cs`

```csharp
public class SecondaryCatchUpService : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.PrimaryNodeAddress))
        {
            _logger.LogInformation("No primary address configured — skipping catch-up (this is a primary).");
            return;
        }

        var myLastLsn = _replicationLogRepository.GetLatestLsn();
        _logger.LogInformation("Starting catch-up from LSN {Lsn} against primary {Primary}", myLastLsn + 1, _options.PrimaryNodeAddress);

        var channel = GrpcChannel.ForAddress(_options.PrimaryNodeAddress, /* TLS options */);
        var client = new Data.DataClient(channel);

        var stream = client.StreamReplicationLog(new StreamReplicationLogRequest { FromLsn = myLastLsn + 1 });

        var count = 0;
        await foreach (var entry in stream.ResponseStream.ReadAllAsync(cancellationToken))
        {
            var dbEntry = MapToDatabaseEntry(entry);
            if (entry.Type == DataType.Null)
                await _writeStorageService.DeleteValue(entry.Key);
            else
                await _writeStorageService.SetValue(entry.Key, dbEntry);

            count++;
        }

        _logger.LogInformation("Catch-up complete. Applied {Count} entries.", count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Key design decisions:
- Uses `IHostedService` so catch-up completes before the node starts serving traffic (startup ordering via `IHostedService` registration order).
- Applies entries through `WriteStorageService` — this means the replication log and all caches on the secondary are updated consistently, exactly as if the writes had arrived live.
- Tombstones (`DataType.Null`) map to `DeleteValue` calls.
- If the primary is unreachable at startup, the service logs a warning and continues — the node will serve potentially stale reads but won't block indefinitely. (Retry policy is a future enhancement.)

---

## Changes to Existing Files

| File | Change |
|---|---|
| `ToyDbContracts/Protos/data.proto` | Add `StreamReplicationLog` RPC + 2 new messages |
| `ToyDb/Services/WriteStorageService.cs` | Inject + call `IReplicationLogRepository` on every write |
| `ToyDb/Extensions/ServiceRegistrationExtensions.cs` | Register `ReplicationLogOptions`, `IReplicationLogRepository`, `ReplicationOptions`, `SecondaryCatchUpService` |
| `ToyDb/Program.cs` | `app.MapGrpcService<ReplicationService>()` |
| `ToyDb/appsettings.json` | Add `ReplicationLog` and `Replication` config sections (empty defaults) |
| `docker-compose.override.yml` | Add `PRIMARY_NODE_ADDRESS` env var to secondary containers |

---

## New Files

```
ToyDb/
  Models/
    ReplicationLogEntry.cs
  Repositories/
    ReplicationLogRepository/
      IReplicationLogRepository.cs
      ReplicationLogRepository.cs
      ReplicationLogOptions.cs
  Services/
    ReplicationService.cs
    CatchUp/
      ReplicationOptions.cs
      SecondaryCatchUpService.cs
ToyDbContracts/
  Protos/
    data.proto  (modified, not new)
```

---

## Docker Configuration

In `docker-compose.override.yml`, add `PRIMARY_NODE_ADDRESS` to secondary containers:

```yaml
toydb-p1-r2:
  environment:
    - PRIMARY_NODE_ADDRESS=https://toydb-p1-r1:8081

toydb-p2-r2:
  environment:
    - PRIMARY_NODE_ADDRESS=https://toydb-p2-r1:8081
```

Primary containers (`toydb-p1-r1`, `toydb-p2-r1`) get no `PRIMARY_NODE_ADDRESS`, so `ReplicationOptions.PrimaryNodeAddress` will be null and catch-up is skipped.

---

## LSN Lifecycle

```
Node start
  └─ ReplicationLogRepository initialises _nextLsn from last entry in file (or 0)

Write arrives (Set or Delete)
  └─ WriteStorageService enqueues
       └─ WAL.Append(key, entry)
       └─ DataStore.Append(key, entry)
       └─ ReplicationLog.Append(key, entry)  → returns LSN, increments _nextLsn
       └─ Cache updates

Secondary startup catch-up
  └─ Read own GetLatestLsn() → e.g. 42
  └─ Call primary StreamReplicationLog(fromLsn: 43)
  └─ Primary streams entries 43, 44, 45...
  └─ Secondary applies each via WriteStorageService
       └─ Each application also appends to secondary's own ReplicationLog
  └─ Secondary's LSN advances to match primary's
```

---

## Limitations & Out of Scope

| Concern | Status |
|---|---|
| Continuous replication (live streaming after catch-up) | Out of scope — routing-coordinated fan-out continues to handle live writes |
| Replication log pruning | Out of scope — log grows unboundedly; prune when all secondaries have confirmed an LSN |
| Catch-up failure / retry on startup | Out of scope — logs warning and continues; dead-letter queue handles live write failures |
| Concurrent catch-up + live writes | Safe — `WriteStorageService` serialises all writes via `ConcurrentQueue`; catch-up entries go through the same queue |
| Log compaction interaction | Safe — compaction only rewrites the DataStore file, not the replication log |
| LSN gap after primary's own log compaction | Not applicable — replication log is never compacted in this phase |

---

## Implementation Order

1. `ReplicationLogEntry` model
2. `IReplicationLogRepository` + `ReplicationLogRepository` + `ReplicationLogOptions`
3. Update `data.proto` → regenerate gRPC stubs
4. Update `WriteStorageService` to write to replication log
5. Implement `ReplicationService` gRPC handler
6. Implement `SecondaryCatchUpService` + `ReplicationOptions`
7. Wire up DI in `ServiceRegistrationExtensions` and `Program.cs`
8. Update `docker-compose.override.yml`
9. Add integration test: write while secondary is down → restart secondary → assert secondary has caught up

---

## Integration Test Scenario

```
1. Start all containers
2. Write key "foo" = "bar" via routing (both replicas receive it)
3. Stop toydb-p1-r2 (secondary)
4. Write key "foo" = "updated" via routing (only primary receives it)
5. Write key "baz" = "qux" via routing (only primary receives it)
6. Restart toydb-p1-r2
7. Wait for catch-up to complete (poll health or add a short delay)
8. Read "foo" directly from toydb-p1-r2 → expect "updated"
9. Read "baz" directly from toydb-p1-r2 → expect "qux"
```
