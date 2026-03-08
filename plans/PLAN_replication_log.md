# Plan: WAL-Based Replication Log on the Primary

> **Date:** February 23, 2026
> **Relates to:** REPLICATION_REVIEW.md — Phase 2, Item 4
> **Goal:** Assign a monotonically-increasing Log Sequence Number (LSN) to each write on the primary, and expose a gRPC streaming endpoint (`StreamReplicationLog(fromLsn)`) that secondaries can consume.

---

## 1. Overview

Today, replication is coordinated entirely by the routing layer — it fans out every write to all replicas simultaneously. If a secondary misses a write (network blip, downtime, restart), that data is permanently lost on that replica.

This plan **repurposes the existing WAL** to serve as both the crash-recovery log and the replication source. This aligns with the idiomatic database pattern (e.g. PostgreSQL WAL shipping) where a single ordered log provides durability, crash recovery, and replication — eliminating the need for a separate replication log file.

### 1.1 Why Repurpose the WAL (Not a Third Log)

The current WAL is not fulfilling its intended role:
- It is written alongside the data store (not ahead of it).
- It is never replayed on startup — `ReadStorageService.RestoreIndexFromStore()` rebuilds exclusively from the data store.
- It is never truncated or compacted.

Introducing a third append-only file alongside the WAL and data store would mean three copies of every write, with the WAL remaining a dead write. Instead, this plan upgrades the WAL to be the authoritative ordered log:

| Component | Role |
|---|---|
| **WAL** | Durable record of every mutation, indexed by LSN. Source for crash recovery and replication streaming. Truncated only after the data store is checkpointed AND all secondaries have acknowledged past that LSN. |
| **Data store** | Compacted, offset-indexed queryable file. Updated from the WAL. Serves reads. |

### 1.2 Scope

This plan covers:
- Adding LSNs to WAL entries
- Implementing crash recovery by replaying the WAL on startup
- Enforcing true write-ahead semantics (WAL written before data store)
- Exposing a gRPC server-streaming endpoint for secondaries to consume WAL entries
- Live-tailing support via an in-process notification channel

This is the foundation for secondary catch-up (Phase 2, Item 5) and improved read consistency (Phase 2, Item 6).

---

## 2. Design

### 2.1 Log Sequence Number (LSN)

- Each ToyDb node maintains a 64-bit (`long`) LSN counter.
- The counter starts at `0` on a fresh node, or is restored to the highest persisted LSN in the WAL on startup.
- Every call to `ExecuteSetValue` or `ExecuteDeleteValue` in `WriteStorageService` increments the counter **before** persisting.
- The counter is managed by a new `ILsnProvider` / `LsnProvider` singleton. Because all writes already flow through a serialised `ConcurrentQueue` in `WriteStorageService`, a simple `Interlocked.Increment` is sufficient — no additional locking is needed.

### 2.2 Updated WAL Entry Format

The WAL entry format is extended to include the LSN and an explicit delete flag:

```text
WAL Entry (new format)
├── Lsn        : long
├── Timestamp  : google.protobuf.Timestamp (int64 binary)
├── Key        : string
├── Type       : DataType
├── Data       : bytes (NullMarker for deletes)
└── IsDelete   : bool
```

### 2.3 Write-Ahead Semantics

The write path changes from the current (non-write-ahead) order:

```text
Current:  WAL.Append → DataStore.Append → Cache updates
          (WAL and DataStore written together — not true write-ahead)
```

To true write-ahead ordering:

```text
New:  LSN = LsnProvider.Next()
      WAL.Append (with LSN)  ← durable first
      DataStore.Append       ← applied second
      Cache updates
      Notifier.Publish       ← notify live-tailing consumers
```

The WAL write is the commit point. If the process crashes after the WAL write but before the data store write, crash recovery will replay the WAL entry into the data store.

### 2.4 Crash Recovery

On startup, `ReadStorageService` (or a new startup service) replays the WAL against the data store:

1. Read the highest LSN from the data store (requires the data store to also persist LSNs — see §2.5).
2. Read all WAL entries with LSN > data store's highest LSN.
3. Apply each missing entry to the data store and update caches.
4. Seed the `LsnProvider` with the WAL's highest LSN.

This replaces the current `RestoreIndexFromStore()` which only rebuilds the offset cache from the data store and ignores the WAL entirely.

### 2.5 Data Store LSN Tracking

To support crash recovery, the data store must track the LSN of the last applied entry:

- Add the LSN field to data store entries (same format extension as the WAL).
- On startup, scan the data store to find its highest LSN — this is the checkpoint boundary.
- WAL entries with LSN > this value are replayed.

### 2.6 WAL Repository Changes

`IWriteAheadLogRepository` is extended with new methods:

- `Append(long lsn, string key, DatabaseEntry entry, bool isDelete)` — writes the LSN-prefixed entry.
- `IEnumerable<WalEntry> ReadFrom(long fromLsn)` — returns all entries with LSN ≥ `fromLsn`, in order.
- `long GetLatestLsn()` — reads the tail of the WAL to find the highest persisted LSN.
- `void TruncateBefore(long lsn)` — removes entries with LSN < `lsn` (used after checkpointing + secondary ACK, future enhancement).

The legacy `Append(string key, DatabaseEntry entry)` (without LSN) remains for use by compaction; all new writes use the LSN-prefixed overload.

### 2.7 gRPC Streaming Endpoint

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
1. On receiving a `StreamReplicationLogRequest`, the node reads all WAL entries with LSN ≥ `from_lsn` and streams them to the caller.
2. After exhausting persisted entries, the stream stays open and pushes new entries in real-time as writes occur (live-tailing).
3. The caller can cancel the stream at any time.

### 2.8 In-Process Notification for Live Tailing

To support live-tailing without polling, `WriteStorageService` publishes new WAL entries to an in-process notification mechanism:

- A new `IReplicationLogNotifier` / `ReplicationLogNotifier` singleton wraps a `Channel<ReplicationLogEntry>` (from `System.Threading.Channels`).
- After each write, `WriteStorageService` writes the entry to the channel.
- The streaming RPC implementation reads from the channel for live entries after it has drained the persisted backlog.

### 2.9 WAL Truncation (Future)

The WAL is not truncated in this phase. A future enhancement adds truncation after:
1. The data store has been checkpointed (compacted) past a given LSN.
2. All secondaries have acknowledged past that LSN.

Until then, the WAL grows unbounded — the same behaviour as today, but now with a clear path to safe truncation.

---

## 3. Implementation Steps

### Step 1 — `LsnProvider`

| Item | Detail |
|---|---|
| **New files** | `ToyDb/Services/ILsnProvider.cs`, `ToyDb/Services/LsnProvider.cs` |
| **Registration** | Singleton in `ServiceRegistrationExtensions` |
| **Startup** | Injected with `IWriteAheadLogRepository` to call `GetLatestLsn()` and seed the counter |
| **API** | `long Next()` — returns `Interlocked.Increment(ref _current)` |

### Step 2 — `WalEntry` model

| Item | Detail |
|---|---|
| **New file** | `ToyDb/Models/WalEntry.cs` |
| **Fields** | `long Lsn`, `Timestamp`, `string Key`, `DataType Type`, `ByteString? Data`, `bool IsDelete` |

### Step 3 — Extend `BaseLogRepository` for LSN-prefixed entries

| Item | Detail |
|---|---|
| **Modified file** | `ToyDb/Repositories/BaseLogRepository.cs` |
| **Changes** | Replace the existing `Append` and `ReadEntry` methods with LSN-aware versions that write `lsn (long)` + `isDelete (bool)` alongside the existing timestamp/key/type/data fields. The new `Append` writes the LSN-prefixed format; the new `ReadEntry` returns a `WalEntry`. |

### Step 4 — Upgrade `WriteAheadLogRepository`

| Item | Detail |
|---|---|
| **Modified files** | `ToyDb/Repositories/WriteAheadLogRepository/IWriteAheadLogRepository.cs`, `WriteAheadLogRepository.cs` |
| **Changes** | Add `Append(long lsn, ...)` overload alongside the existing `Append`. Add `ReadFrom(long fromLsn)` and `GetLatestLsn()` methods. The `ReadFrom` implementation scans the WAL file sequentially, yielding entries with LSN ≥ the requested value. |

### Step 5 — Upgrade data store to include LSN

| Item | Detail |
|---|---|
| **Modified files** | `ToyDb/Repositories/DataStoreRepository/IDataStoreRepository.cs`, `DataStoreRepository.cs` |
| **Changes** | Add `Append(long lsn, ...)` overload and `GetLatestLsn()`. Data store entries now include the LSN field so the checkpoint boundary can be determined on startup. `AppendRange` (used by compaction) is updated to preserve LSNs. |

### Step 6 — `ReplicationLogNotifier`

| Item | Detail |
|---|---|
| **New files** | `ToyDb/Services/IReplicationLogNotifier.cs`, `ToyDb/Services/ReplicationLogNotifier.cs` |
| **Implementation** | Wraps `Channel<ReplicationLogEntry>` (unbounded). `Publish(WalEntry)` writes to the channel. `ReadAllAsync(CancellationToken)` returns the channel reader's async enumerable. |
| **Registration** | Singleton |

### Step 7 — Wire into `WriteStorageService`

| Item | Detail |
|---|---|
| **Modified file** | `ToyDb/Services/WriteStorageService.cs` |
| **Changes** | Inject `ILsnProvider` and `IReplicationLogNotifier`. Reorder `ExecuteSetValue` and `ExecuteDeleteValue` to write WAL **first** (true write-ahead), then data store, then caches, then notify. Use the LSN-prefixed `Append(long lsn, ...)` overload for both WAL and data store writes. |
| **Interface** | `IWriteStorageService` unchanged — LSN assignment is an internal concern. |

### Step 8 — Implement crash recovery

| Item | Detail |
|---|---|
| **New file** | `ToyDb/Services/WalRecoveryService.cs` |
| **Modified file** | `ToyDb/Services/ReadStorageService.cs` |
| **Changes** | Replace `RestoreIndexFromStore()` with a recovery process that: (1) reads the data store's latest LSN, (2) replays WAL entries with LSN > that value into the data store, (3) rebuilds the offset and entry caches. This can be a method on `ReadStorageService` or a dedicated startup service. |

### Step 9 — Proto & gRPC streaming endpoint

| Item | Detail |
|---|---|
| **Modified file** | `ToyDbContracts/Protos/data.proto` |
| **Changes** | Add `StreamReplicationLog` RPC, `StreamReplicationLogRequest` message, `ReplicationLogEntry` message (as described in §2.7). |

### Step 10 — Implement `StreamReplicationLog` in `ClientService`

| Item | Detail |
|---|---|
| **Modified file** | `ToyDb/Services/ClientService.cs` |
| **Changes** | Add override for `StreamReplicationLog`. Inject `IWriteAheadLogRepository` and `IReplicationLogNotifier`. First drain persisted entries via `ReadFrom(fromLsn)`, streaming each to the caller. Then switch to live-tailing via `_notifier.ReadAllAsync(context.CancellationToken)`. |

### Step 11 — Configuration & DI wiring

| Item | Detail |
|---|---|
| **Modified files** | `ToyDb/Extensions/ServiceRegistrationExtensions.cs` |
| **Changes** | Register `ILsnProvider` and `IReplicationLogNotifier` as singletons. No new config sections needed — WAL config already exists. |

### Step 12 — Unit tests

| Item | Detail |
|---|---|
| **New files** | `ToyDbUnitTests/Services/LsnProviderTests.cs`, `ToyDbUnitTests/Services/ReplicationLogNotifierTests.cs`, `ToyDbUnitTests/Services/WalRecoveryServiceTests.cs` |
| **Coverage** | LSN monotonicity, LSN restoration on startup, WAL append & read-from with LSN, crash recovery replay, notifier publish/subscribe, streaming RPC with a mock. |

Test naming follows `GivenX_WhenY_ThenZ` convention per project standards.

### Step 13 — Integration tests

| Item | Detail |
|---|---|
| **New/modified files** | `ToyDbIntegrationTests/Tests/` |
| **Coverage** | Write a value via the routing layer, then call `StreamReplicationLog(fromLsn: 0)` on the primary and verify the entry appears. Verify LSN ordering across multiple writes. Crash recovery: write entries, kill the data store mid-write, restart, verify data store is consistent with WAL. |

---

## 4. Binary Format — WAL Entry (Updated)

```text
┌──────────┬──────────────┬────────────┬──────────┬──────────┬──────────┐
│ LSN      │ Timestamp    │ Key        │ DataType │ Data     │ IsDelete │
│ (int64)  │ (int64)      │ (string)   │ (string) │ (string) │ (bool)   │
│ 8 bytes  │ 8 bytes      │ length-    │ length-  │ length-  │ 1 byte   │
│          │              │ prefixed   │ prefixed │ prefixed │          │
└──────────┴──────────────┴────────────┴──────────┴──────────┴──────────┘
```

This extends the existing `BaseLogRepository` format by prepending the LSN and appending the IsDelete flag. Both the WAL and data store use this format so the checkpoint LSN can be determined from the data store.

---

## 5. File Summary

| Action | Path |
|---|---|
| **Create** | `ToyDb/Services/ILsnProvider.cs` |
| **Create** | `ToyDb/Services/LsnProvider.cs` |
| **Create** | `ToyDb/Models/WalEntry.cs` |
| **Create** | `ToyDb/Services/IReplicationLogNotifier.cs` |
| **Create** | `ToyDb/Services/ReplicationLogNotifier.cs` |
| **Create** | `ToyDb/Services/WalRecoveryService.cs` |
| **Create** | `ToyDbUnitTests/Services/LsnProviderTests.cs` |
| **Create** | `ToyDbUnitTests/Services/ReplicationLogNotifierTests.cs` |
| **Create** | `ToyDbUnitTests/Services/WalRecoveryServiceTests.cs` |
| **Modify** | `ToyDb/Repositories/BaseLogRepository.cs` |
| **Modify** | `ToyDb/Repositories/WriteAheadLogRepository/IWriteAheadLogRepository.cs` |
| **Modify** | `ToyDb/Repositories/WriteAheadLogRepository/WriteAheadLogRepository.cs` |
| **Modify** | `ToyDb/Repositories/DataStoreRepository/IDataStoreRepository.cs` |
| **Modify** | `ToyDb/Repositories/DataStoreRepository/DataStoreRepository.cs` |
| **Modify** | `ToyDb/Services/WriteStorageService.cs` |
| **Modify** | `ToyDb/Services/ReadStorageService.cs` |
| **Modify** | `ToyDb/Services/ClientService.cs` |
| **Modify** | `ToyDbContracts/Protos/data.proto` |
| **Modify** | `ToyDb/Extensions/ServiceRegistrationExtensions.cs` |
| **Delete** | `ToyDb/Repositories/ReplicationLogRepository/` (not created — WAL serves this role) |

---

## 6. Open Questions & Future Considerations

| Topic | Notes |
|---|---|
| **WAL truncation** | The WAL grows unbounded in this phase. Truncation requires tracking the minimum of (a) the data store checkpoint LSN and (b) the lowest acknowledged LSN across all secondaries. This is deferred to the secondary catch-up implementation. |
| **Compaction interaction** | Data store compaction creates a new compacted file. After compaction, the data store's effective checkpoint LSN is the highest LSN in the compacted file. WAL entries before this LSN are eligible for truncation (once secondaries ACK). |
| **LSN scope** | LSNs are per-node, not global. This is sufficient for single-primary-per-partition replication. If multi-primary or partition re-assignment is introduced later, a global LSN or epoch scheme would be needed. |
| **Backpressure** | The `Channel<WalEntry>` in the notifier is unbounded. If a streaming consumer is slow, memory could grow. A bounded channel with a drop-oldest policy or disconnect-on-full strategy may be warranted later. |
| **Snapshot + LSN** | For large datasets, streaming from LSN 0 is expensive. A future optimisation is to support snapshot transfer (bulk data store) plus incremental WAL replay from the snapshot's LSN. |
| **Routing layer changes** | This plan does **not** change the routing layer. Secondaries will still receive writes from routing in parallel. The WAL replication stream exists as a catch-up mechanism. A future step could shift to primary-only writes with secondary pull-based replication, removing the routing fan-out entirely. |
| **Fsync / durability** | The current `FileStream` writes do not explicitly `Flush(true)` / fsync. For true write-ahead durability, the WAL write should fsync before proceeding to the data store write. This is a performance trade-off to evaluate. |
