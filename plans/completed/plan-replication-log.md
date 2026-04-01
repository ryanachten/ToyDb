# Plan: WAL-Based Replication Log on the Primary

## Objective
Assign a monotonically-increasing Log Sequence Number (LSN) to each write on the primary, and expose a gRPC streaming endpoint (`StreamReplicationLog(fromLsn)`) that secondaries can consume.

## Context & Background
Today, replication is coordinated entirely by the routing layer. If a secondary misses a write, that data is permanently lost. This plan repurposes the existing WAL to serve as both the crash-recovery log and the replication source, aligning with idiomatic database patterns.

## Architecture & Design

### 1. Log Sequence Number (LSN)
- Each ToyDb node maintains a 64-bit (`long`) LSN counter.
- Counter starts at 0 or is restored to the highest persisted LSN on startup.
- Managed by `ILsnProvider`.

### 2. Write-Ahead Semantics
The write path changes to true write-ahead ordering:
`LSN -> WAL.Append (with LSN) -> DataStore.Append -> Cache updates -> Notifier.Publish`

### 3. Crash Recovery
Replays the WAL against the data store on startup for any entries with LSN > data store's highest LSN.

### 4. gRPC Streaming Endpoint
New `StreamReplicationLog` RPC in `Data.proto` for secondaries to consume WAL entries.

### 5. In-Process Notification
`ReplicationLogNotifier` maintains per-subscriber `Channel<WalEntry>` for live-tailing.

## Implementation Steps

1. **`LsnProvider`**: Create singleton to manage LSN increments and restoration.
2. **`WalEntry` model**: Extend format to include LSN, Timestamp, Key, Type, Data, and IsDelete.
3. **`BaseLogRepository` upgrade**: Implement LSN-prefixed entry writing and reading.
4. **`WriteAheadLogRepository` extension**: Add `ReadFrom(long fromLsn)` and `GetLatestLsn()`.
5. **Data Store upgrade**: Include LSN in entries to track checkpoint boundaries.
6. **`ReplicationLogNotifier`**: Implement bounded channel fan-out for live-tailing.
7. **`WriteStorageService` wiring**: Implement true write-ahead ordering.
8. **Crash Recovery implementation**: Replay missing WAL entries into data store on startup.
9. **Proto & gRPC streaming**: Define and implement `StreamReplicationLog` endpoint.

## Verification & Testing
- **Unit Tests**: LSN monotonicity, restoration on startup, WAL append/read, crash recovery replay, notifier publish/subscribe.
- **Integration Tests**: Verify entry appearance in stream after writes, LSN ordering, and crash recovery consistency.

## Binary Format — WAL Entry (Updated)
```text
┌──────────┬──────────────┬────────────┬──────────┬──────────┬──────────┐
│ LSN      │ Timestamp    │ Key        │ DataType │ Data     │ IsDelete │
│ (int64)  │ (int64)      │ (string)   │ (string) │ (string) │ (bool)   │
│ 8 bytes  │ 8 bytes      │ length-    │ length-  │ length-  │ 1 byte   │
│          │              │ prefixed   │ prefixed │ prefixed │          │
└──────────┴──────────────┴────────────┴──────────┴──────────┴──────────┘
```

## References
- `REPLICATION_REVIEW.md` — Phase 2, Item 4
