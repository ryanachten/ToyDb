# Plan: Replication Log / Changelog on the Primary

> **Date:** February 23, 2026
> **Relates to:** REPLICATION_REVIEW.md — Phase 2, Item 4
> **Goal:** Assign a monotonically-increasing Log Sequence Number (LSN) to each write on the primary, and expose a gRPC streaming endpoint (`StreamReplicationLog(fromLsn)`) that secondaries can consume.

---

## 1. Overview

Today, replication is coordinated entirely by the routing layer — it fans out every write to all replicas simultaneously. If a secondary misses a write (network blip, downtime, restart), that data is permanently lost on that replica.

This plan introduces a **replication log** on each ToyDb node. Every mutation (set or delete) is assigned a monotonically-increasing LSN before being persisted. A new gRPC server-streaming endpoint allows secondaries to subscribe to the log from a given LSN and receive all subsequent entries in order. This is the foundation for secondary catch-up (Phase 2, Item 5) and improved read consistency (Phase 2, Item 6).

---

## 2. Design

### 2.1 Log Sequence Number (LSN)

- Each ToyDb node maintains a 64-bit (`long`) LSN counter.
- The counter starts at `0` on a fresh node, or is restored to the highest persisted LSN on startup.
- Every call to `ExecuteSetValue` or `ExecuteDeleteValue` in `WriteStorageService` increments the counter **before** persisting, and the LSN is written alongside the entry in both the WAL and data store.
- The counter is managed by a new `ILsnProvider` / `LsnProvider` singleton. Because all writes already flow through a serialised `ConcurrentQueue` in `WriteStorageService`, a simple `Interlocked.Increment` is sufficient — no additional locking is needed.

### 2.2 Replication Log Entry

A replication log entry captures the full mutation:

```
ReplicationLogEntry
├── Lsn        : long
├── Timestamp  : google.protobuf.Timestamp
├── Key        : string
├── Type       : DataType
├── Data       : bytes (null for deletes)
└── IsDelete   : bool
```

### 2.3 Replication Log Storage

The replication log is a **separate append-only file** (not the WAL or the data store):

- Location: `bin/replication-log/{NODE_NAME}/` (configurable via `appsettings.json`).
- Format: the same binary encoding as `BaseLogRepository.Append`, extended with an LSN prefix field.
- Unlike the data store, the replication log is **never compacted** — it is a complete ordered history. A future enhancement can add log truncation after all secondaries have acknowledged past a given LSN.

A new repository class `ReplicationLogRepository` (extending `BaseLogRepository`) handles persistence. It adds:
- `AppendWithLsn(long lsn, string key, DatabaseEntry entry)` — writes LSN + entry.
- `ReadFrom(long fromLsn)` — returns an `IEnumerable<ReplicationLogEntry>` starting from the first entry with LSN ≥ `fromLsn`.
- `GetLatestLsn()` — scans the tail of the log to determine the highest persisted LSN (used on startup).

### 2.4 gRPC Streaming Endpoint

A new RPC is added to `Data.proto`:

```protobuf
service Data {
    // ... existing RPCs ...
    rpc StreamReplicationLog (StreamReplicationLogRequest) returns (stream ReplicationLogEntry);
}

message StreamReplicationLogRequest {
    int64 from_lsn = 1;
}

message ReplicationLogEntry {
    int64 lsn = 1;
    google.protobuf.Timestamp timestamp = 2;
    string key = 3;
    DataType type = 4;
    google.protobuf.BytesValue value = 5;
    bool is_delete = 6;
}
```

**Behaviour:**
1. On receiving a `StreamReplicationLogRequest`, the node reads all replication log entries with LSN ≥ `from_lsn` and streams them to the caller.
2. After exhausting persisted entries, the stream stays open and pushes new entries in real-time as writes occur (live-tailing).
3. The caller can cancel the stream at any time.

### 2.5 In-Process Notification for Live Tailing

To support live-tailing without polling, `WriteStorageService` publishes new replication log entries to an in-process notification mechanism:

- A new `IReplicationLogNotifier` / `ReplicationLogNotifier` singleton wraps a `Channel<ReplicationLogEntry>` (from `System.Threading.Channels`).
- After each write, `WriteStorageService` writes the entry to the channel.
- The streaming RPC implementation reads from the channel for live entries after it has drained the persisted backlog.

### 2.6 Integration into the Write Path

Current write path in `WriteStorageService.ExecuteSetValue`:

```
WAL.Append → DataStore.Append → Cache updates
```

New write path:

```
LSN = LsnProvider.Next()
WAL.Append → DataStore.Append → ReplicationLog.AppendWithLsn → Cache updates → Notifier.Publish
```

The same applies to `ExecuteDeleteValue` and `ExecuteCompactLogs` (compaction events should **not** generate replication log entries — they are local optimisations).

---

## 3. Implementation Steps

### Step 1 — `LsnProvider`

| Item | Detail |
|---|---|
| **New files** | `ToyDb/Services/ILsnProvider.cs`, `ToyDb/Services/LsnProvider.cs` |
| **Registration** | Singleton in `ServiceRegistrationExtensions` |
| **Startup** | Injected with `IReplicationLogRepository` to call `GetLatestLsn()` and seed the counter |
| **API** | `long Next()` — returns `Interlocked.Increment(ref _current)` |

### Step 2 — `ReplicationLogEntry` model

| Item | Detail |
|---|---|
| **New file** | `ToyDb/Models/ReplicationLogEntry.cs` |
| **Fields** | `long Lsn`, `Timestamp`, `string Key`, `DataType Type`, `ByteString? Data`, `bool IsDelete` |

### Step 3 — `ReplicationLogRepository`

| Item | Detail |
|---|---|
| **New files** | `ToyDb/Repositories/ReplicationLogRepository/IReplicationLogRepository.cs`, `ReplicationLogRepository.cs`, `ReplicationLogOptions.cs` |
| **Extends** | `BaseLogRepository` (may need minor refactoring to support the LSN prefix in the binary format) |
| **Config** | New `appsettings.json` section: `"ReplicationLog": { "Location": "bin/replication-log" }` |
| **Methods** | `AppendWithLsn(long lsn, string key, DatabaseEntry entry)`, `IEnumerable<ReplicationLogEntry> ReadFrom(long fromLsn)`, `long GetLatestLsn()` |

### Step 4 — `ReplicationLogNotifier`

| Item | Detail |
|---|---|
| **New files** | `ToyDb/Services/IReplicationLogNotifier.cs`, `ToyDb/Services/ReplicationLogNotifier.cs` |
| **Implementation** | Wraps `Channel<ReplicationLogEntry>` (unbounded). `Publish(ReplicationLogEntry)` writes to the channel. `ReadAllAsync(CancellationToken)` returns the channel reader's async enumerable. |
| **Registration** | Singleton |

### Step 5 — Wire into `WriteStorageService`

| Item | Detail |
|---|---|
| **Modified file** | `ToyDb/Services/WriteStorageService.cs` |
| **Changes** | Inject `ILsnProvider`, `IReplicationLogRepository`, `IReplicationLogNotifier`. In `ExecuteSetValue` and `ExecuteDeleteValue`, after WAL + data store writes, call `_replicationLogRepository.AppendWithLsn(lsn, key, entry)` and `_notifier.Publish(...)`. |
| **Interface** | `IWriteStorageService` unchanged — LSN assignment is an internal concern. |

### Step 6 — Extend `BaseLogRepository` for LSN-prefixed entries

| Item | Detail |
|---|---|
| **Modified file** | `ToyDb/Repositories/BaseLogRepository.cs` |
| **Changes** | Add a protected `AppendWithLsn` method that writes `lsn (long)` before the existing timestamp/key/type/data fields. Add a corresponding `ReadEntryWithLsn` method. Existing `Append` and `ReadEntry` methods remain unchanged to avoid breaking WAL and data store. |

### Step 7 — Proto & gRPC streaming endpoint

| Item | Detail |
|---|---|
| **Modified file** | `ToyDbContracts/Protos/data.proto` |
| **Changes** | Add `StreamReplicationLog` RPC, `StreamReplicationLogRequest` message, `ReplicationLogEntry` message (as described in §2.4). |
| **New file** | None — `ClientService.cs` in ToyDb already implements `Data.DataBase`; add the `StreamReplicationLog` override there. |

### Step 8 — Implement `StreamReplicationLog` in `ClientService`

| Item | Detail |
|---|---|
| **Modified file** | `ToyDb/Services/ClientService.cs` |
| **Changes** | Add override for `StreamReplicationLog`. Inject `IReplicationLogRepository` and `IReplicationLogNotifier`. First drain persisted entries via `ReadFrom(fromLsn)`, streaming each to the caller. Then switch to live-tailing via `_notifier.ReadAllAsync(context.CancellationToken)`. |

### Step 9 — Configuration & DI wiring

| Item | Detail |
|---|---|
| **Modified files** | `ToyDb/appsettings.json`, `ToyDb/Extensions/ServiceRegistrationExtensions.cs` |
| **Changes** | Add `ReplicationLog` config section. Register `ReplicationLogOptions`, `IReplicationLogRepository`, `ILsnProvider`, `IReplicationLogNotifier` as singletons. |

### Step 10 — Unit tests

| Item | Detail |
|---|---|
| **New files** | Tests under `ToyDbUnitTests/Services/LsnProviderTests.cs`, `ToyDbUnitTests/Services/ReplicationLogNotifierTests.cs` |
| **Coverage** | LSN monotonicity, LSN restoration on startup, replication log append & read-from, notifier publish/subscribe, streaming RPC end-to-end with a mock. |

Test naming follows `GivenX_WhenY_ThenZ` convention per project standards.

### Step 11 — Integration tests

| Item | Detail |
|---|---|
| **New/modified files** | `ToyDbIntegrationTests/Tests/` |
| **Coverage** | Write a value via the routing layer, then call `StreamReplicationLog(fromLsn: 0)` on the primary and verify the entry appears. Verify LSN ordering across multiple writes. |

---

## 4. Binary Format — Replication Log Entry

```
┌──────────┬──────────────┬────────────┬──────────┬──────────┬──────────┐
│ LSN      │ Timestamp    │ Key        │ DataType │ Data     │ IsDelete │
│ (int64)  │ (int64)      │ (string)   │ (string) │ (string) │ (bool)   │
│ 8 bytes  │ 8 bytes      │ length-    │ length-  │ length-  │ 1 byte   │
│          │              │ prefixed   │ prefixed │ prefixed │          │
└──────────┴──────────────┴────────────┴──────────┴──────────┴──────────┘
```

This extends the existing `BaseLogRepository` format by prepending the LSN and appending the IsDelete flag.

---

## 5. File Summary

| Action | Path |
|---|---|
| **Create** | `ToyDb/Services/ILsnProvider.cs` |
| **Create** | `ToyDb/Services/LsnProvider.cs` |
| **Create** | `ToyDb/Models/ReplicationLogEntry.cs` |
| **Create** | `ToyDb/Repositories/ReplicationLogRepository/IReplicationLogRepository.cs` |
| **Create** | `ToyDb/Repositories/ReplicationLogRepository/ReplicationLogRepository.cs` |
| **Create** | `ToyDb/Repositories/ReplicationLogRepository/ReplicationLogOptions.cs` |
| **Create** | `ToyDb/Services/IReplicationLogNotifier.cs` |
| **Create** | `ToyDb/Services/ReplicationLogNotifier.cs` |
| **Create** | `ToyDbUnitTests/Services/LsnProviderTests.cs` |
| **Create** | `ToyDbUnitTests/Services/ReplicationLogNotifierTests.cs` |
| **Modify** | `ToyDb/Repositories/BaseLogRepository.cs` |
| **Modify** | `ToyDb/Services/WriteStorageService.cs` |
| **Modify** | `ToyDb/Services/ClientService.cs` |
| **Modify** | `ToyDbContracts/Protos/data.proto` |
| **Modify** | `ToyDb/appsettings.json` |
| **Modify** | `ToyDb/Extensions/ServiceRegistrationExtensions.cs` |

---

## 6. Open Questions & Future Considerations

| Topic | Notes |
|---|---|
| **Log truncation** | The replication log grows unbounded. Once secondary catch-up (Item 5) is implemented, truncation can be added after all secondaries acknowledge past a checkpoint LSN. |
| **Compaction interaction** | Log compaction in the data store does not affect the replication log. The replication log is the authoritative ordered history. |
| **LSN scope** | LSNs are per-node, not global. This is sufficient for single-primary-per-partition replication. If multi-primary or partition re-assignment is introduced later, a global LSN or epoch scheme would be needed. |
| **Backpressure** | The `Channel<ReplicationLogEntry>` in the notifier is unbounded. If a streaming consumer is slow, memory could grow. A bounded channel with a drop-oldest policy or disconnect-on-full strategy may be warranted later. |
| **Snapshot + LSN** | For large datasets, streaming from LSN 0 is expensive. A future optimisation is to support snapshot transfer (bulk data) plus incremental log replay from the snapshot's LSN. |
| **Routing layer changes** | This plan does **not** change the routing layer. Secondaries will still receive writes from routing in parallel. The replication log exists as a catch-up mechanism. A future step could shift to primary-only writes with secondary pull-based replication, removing the routing fan-out entirely. |
