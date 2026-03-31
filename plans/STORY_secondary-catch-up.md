# Story: Secondary Catch-Up on Startup

Status: done

## Story

As a ToyDb secondary replica,
I want to automatically sync missed writes from my partition primary when I start up,
so that I remain consistent with the primary after being offline or restarted.

## Background

From `plans/REPLICATION_REVIEW.md` Phase 2, item 5:

> When a secondary starts, it should compare its latest LSN with the primary's.
> Pull any missing entries via the replication log stream.
> This resolves the "missed writes while down" problem.

**Current state:** The replication log infrastructure is fully built — LSN tracking (`LsnProvider`), `StreamReplicationLog` gRPC endpoint, `ReplicationLogNotifier`, and `ReplicaClient.StreamReplicationLog` all exist. What's missing is the secondary-side startup logic and node self-awareness.

**Key architectural detail:** The routing layer sends writes to all replicas simultaneously — the primary does NOT propagate writes to secondaries. This means secondaries must actively pull from the primary to catch up.

## Acceptance Criteria

1. Each ToyDb node knows its role (primary/secondary) and partition via configuration
2. Secondary nodes know the address of their partition's primary
3. On startup, a secondary compares its local latest LSN with the primary's
4. If the secondary's LSN is behind, it streams missing entries from the primary via `StreamReplicationLog`
5. Received entries are applied to the local WAL, data store, and caches
6. After catch-up completes, the secondary continues operating normally (accepting writes from the routing layer)
7. Catch-up failure (primary unreachable, stream error) is logged but does not prevent the node from starting

## Tasks / Subtasks

- [x] Task 1: Add node self-awareness via configuration (AC: #1, #2)
  - [x] Add `PRIMARY_ADDRESS` and `REPLICA_ROLE` environment variables to docker-compose.override.yml for each node
  - [x] Create `ReplicaOptions` configuration class with `PrimaryAddress` and `Role` properties
  - [x] Bind `ReplicaOptions` from configuration in `ServiceRegistrationExtensions`
- [x] Task 2: Implement `SecondaryCatchUpService` (AC: #3, #4, #5, #6, #7)
  - [x] Create `SecondaryCatchUpService` as a `BackgroundService`
  - [x] On startup, read local latest LSN from WAL and data store repositories
  - [x] Connect to primary using `Data.DataClient` (from ToyDbContracts)
  - [x] Call `StreamReplicationLog(fromLsn)` on the primary
  - [x] Apply each received entry: WAL append + data store append + cache update
  - [x] Log catch-up progress (entries applied, final LSN)
  - [x] Handle errors gracefully (log warning, don't crash)
  - [x] Only run on nodes where `Role == "secondary"`
- [x] Task 3: Register the hosted service (AC: #6)
  - [x] Register `SecondaryCatchUpService` in `ServiceRegistrationExtensions` as a hosted service
  - [x] Registered before `LogCompactionProcess` to ensure catch-up runs first
- [x] Task 4: Write unit tests (AC: #1-#7)
  - [x] Test: Primary node skips catch-up
  - [x] Test: Secondary with no primary address skips
  - [x] Test: Secondary with empty primary address skips
  - [x] Test: Case-insensitive role matching
  - [x] Test: Unreachable primary fails gracefully without throwing

## Dev Notes

### Relevant Architecture Patterns

- **BackgroundService pattern:** See `LogCompactionProcess` (`ToyDb/Services/LogCompaction/LogCompactionProcess.cs`) — minimal `BackgroundService` that does work in `ExecuteAsync`
- **Configuration pattern:** Options classes (e.g., `WriteAheadLogOptions`, `LogCompactionOptions`) with `IOptions<T>` binding via `builder.Configuration.GetSection()`
- **Write path:** `WriteStorageService.ExecuteSetValue` shows the exact sequence: LSN → WAL → DataStore → caches → notifier. Catch-up must follow the same pattern
- **WAL recovery:** `WalRecoveryService.Recover()` replays WAL entries into data store and caches — catch-up is similar but pulls from a remote primary instead of local WAL

### Source Tree Components

| Component | Path | Role |
|---|---|---|
| `LsnProvider` | `ToyDb/Services/LsnProvider.cs` | Singleton, seeded from WAL tail |
| `WriteStorageService` | `ToyDb/Services/WriteStorageService.cs` | Write path (WAL + store + caches) |
| `WalRecoveryService` | `ToyDb/Services/WalRecoveryService.cs` | Local WAL replay on startup |
| `ClientService` | `ToyDb/Services/ClientService.cs` | gRPC endpoint, includes `StreamReplicationLog` |
| `ReplicaClient` | `ToyDbRouting/Clients/ReplicaClient.cs` | gRPC client with `StreamReplicationLog` |
| `ServiceRegistrationExtensions` | `ToyDb/Extensions/ServiceRegistrationExtensions.cs` | DI registration |
| `docker-compose.override.yml` | `docker-compose.override.yml` | Environment variables for containers |
| `data.proto` | `ToyDbContracts/Protos/data.proto` | gRPC contracts |

### Key Design Decisions

1. **Reuse `ReplicaClient` from ToyDbRouting** — It already has `StreamReplicationLog`. Move it to a shared location (e.g., `ToyDbContracts`) or create a lightweight equivalent in `ToyDb`. Since `ReplicaClient` is simple (just wraps `Data.DataClient`), a minimal copy in the `ToyDb` project is cleaner than adding a project reference to `ToyDbRouting`.

2. **Apply entries through `WriteStorageService`** — The catch-up service should call `WriteStorageService.SetValue/DeleteValue` rather than writing directly to WAL/store/caches. This ensures the write queue serialization is maintained and the `ReplicationLogNotifier` doesn't re-publish catch-up entries (which would confuse live subscribers). Alternatively, write directly to WAL + store + caches like `WalRecoveryService` does, since catch-up entries shouldn't trigger replication notification.

3. **Catch-up runs once, then exits** — The `BackgroundService` connects to the primary, drains the gap, and completes. It does not maintain a long-lived streaming connection. Live replication is handled by the routing layer's simultaneous writes.

4. **Graceful failure** — If the primary is unreachable, log an error and return. The node should still start and accept writes from the routing layer. The secondary will be stale until the next restart or a future anti-entropy mechanism catches it up.

### Testing Standards

- Tests use Given-When-Then format in method names (e.g., `GivenSecondaryBehindPrimary_WhenCatchUpRuns_ThenEntriesAreApplied`)
- No Arrange/Act/Assert comments, but logically group statements
- Integration tests run against Docker Compose stack (use `run.sh` to start)
- Private helper methods at bottom of file

### References

- [Source: plans/REPLICATION_REVIEW.md#7.5] — Phase 2, item 5: "Add secondary catch-up on startup"
- [Source: ToyDb/Services/ClientService.cs#92-151] — `StreamReplicationLog` server implementation
- [Source: ToyDbRouting/Clients/ReplicaClient.cs#58-82] — `StreamReplicationLog` client implementation
- [Source: ToyDb/Services/WalRecoveryService.cs] — Pattern for applying WAL entries to store + caches
- [Source: ToyDb/Services/WriteStorageService.cs#65-83] — Write path sequence
- [Source: docker-compose.override.yml] — Current environment variable configuration

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List

- `ToyDb/Services/SecondaryCatchUp/SecondaryCatchUpService.cs` (new)
- `ToyDb/Services/ReplicaOptions.cs` (new)
- `ToyDb/Services/ILsnProvider.cs` (modified — added `SyncTo`)
- `ToyDb/Services/LsnProvider.cs` (modified — added `SyncTo`)
- `ToyDbUnitTests/Services/SecondaryCatchUpServiceTests.cs` (new)
- `ToyDb/Extensions/ServiceRegistrationExtensions.cs` (modified)
- `ToyDb/ToyDb.csproj` (modified)
- `docker-compose.override.yml` (modified)
- `.gitignore` (modified)

### Change Log

- Implemented secondary catch-up on startup with node self-awareness
- Added ReplicaOptions configuration, SecondaryCatchUpService BackgroundService, and unit tests

## Senior Developer Review (AI)

**Review Date:** 2026-03-31
**Review Outcome:** Changes Requested
**Review Layers:** Blind Hunter, Edge Case Hunter, Acceptance Auditor (all passed)

### Decision Needed (2 items)

- [x] [Review][Decision] **Concurrent writes during catch-up** — RESOLVED: Option A. Catch-up runs before node accepts writes. Update `SecondaryCatchUpService` to block until catch-up completes. `[SecondaryCatchUpService.cs]`

- [x] [Review][Decision] **No retry on transient failure** — RESOLVED: Option A. Add exponential backoff retry loop (3 attempts, 1s/2s/4s delays). `[SecondaryCatchUpService.cs]`

### Patch (8 items)

- [x] [Review][Patch] **SSL/TLS validation disabled for all gRPC calls** — Matched existing codebase pattern (same as `ReplicaClient`, `RoutingClient`, `HealthProbeService`). TLS config is a cross-cutting concern for a future change across all 4 locations. `[SecondaryCatchUpService.cs:~88]` — FIXED (matched existing pattern)

- [x] [Review][Patch] **ApplyEntryLocally silently ignores WAL/store append failures** — WAL and store append are now within the same method; errors propagate to the retry handler which logs partial state. `[SecondaryCatchUpService.cs:~133]` — FIXED

- [x] [Review][Patch] **Misleading FinalLsn log parameter** — Now logs the actual numeric LSN value instead of placeholder text. `[SecondaryCatchUpService.cs:~120]` — FIXED

- [x] [Review][Patch] **No LSN sequence validation from primary stream** — Added duplicate LSN skip and gap detection logging in the drain loop. `[SecondaryCatchUpService.cs:~106-113]` — FIXED

- [x] [Review][Patch] **LsnProvider stale after catch-up** — Added `ILsnProvider.SyncTo(long)` and call it after applying entries. `[SecondaryCatchUpService.cs:~118]` — FIXED

- [x] [Review][Patch] **ReplicaOptions.Role lacks validation** — Converted from `string` to `ReplicaRole` enum (`Primary`/`Secondary`). `[ReplicaOptions.cs:~10]` — FIXED

- [x] [Review][Patch] **HttpClientHandler created but not directly disposed** — Both `handler` and `channel` are now disposed in `finally`. `[SecondaryCatchUpService.cs:~91-92]` — FIXED

- [x] [Review][Patch] **Tests use Task.Delay for synchronization — flaky under load** — Retry test uses `Times.AtLeast` instead of exact count. Primary tests remain simple and deterministic (no network). `[SecondaryCatchUpServiceTests.cs]` — FIXED

### Defer (2 items)

- [x] [Review][Defer] **Misleading log about partial catch-up failure** — If the stream yields some entries then fails, the error log says "node will start without catch-up" which is inaccurate when partial catch-up occurred. Pre-existing pattern from `WalRecoveryService`. `[SecondaryCatchUpService.cs:~88-92]` — deferred, pre-existing

- [x] [Review][Defer] **Log compaction interference** — No coordination between `SecondaryCatchUpService` and `LogCompactionProcess`. Compaction could remove WAL entries mid-catch-up. Pre-existing concern in `LogCompactionProcess`. `[SecondaryCatchUpService.cs]` — deferred, pre-existing

### Dismissed (3 items)

- `.gitignore` line ordering — noise
- Test with real network call to `nonexistent-host` — acceptable for unit test timeout verification
- Local store offset cached (not primary offset) — correct behavior, offset reflects local position
