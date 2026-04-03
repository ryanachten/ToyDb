# Plan: Phase 3 — High Availability (Leader Election)

## Scope
This plan covers **Steps 1–5: node self-awareness and leader election**. Routing redundancy (Steps 6–7 from the original review) will be addressed in a follow-up plan (`plan-phase3b-routing-redundancy.md`).

## Objective
Enable ToyDb to survive primary node failures through node self-awareness and leader election, eliminating static role assignment.

## Context & Background
The replication review (`plans/review-replication.md`) identified that primary/secondary roles are statically assigned via environment variables. If a primary fails, writes for that partition are permanently blocked until manual intervention. Phase 3 addresses the election and failover gaps; routing redundancy is deferred.

Phase 1 (resilience/retries) and Phase 2 (LSN catch-up) are complete, providing the foundation: health probing, replication log streaming with LSN ordering, secondary catch-up, and dead-letter retry.

## Architecture & Design

### Design Decisions

**Topology: 3 replicas per partition (1 primary, 2 secondaries)**
- Majority election (2/3) allows failover even when one node is down.
- The existing `toydb-p1-r1`, `toydb-p1-r2`, `toydb-p2-r1`, `toydb-p2-r2` remain; two new replicas are added (`toydb-p1-r3`, `toydb-p2-r3`).

**Election Protocol: Term-based leader election with LSN priority**
- Nodes track a monotonically increasing `Term` number (persisted in WAL metadata).
- When a secondary detects primary failure via failed replication stream or health probe, it increments the term and requests votes from peers.
- The secondary with the highest last-applied LSN wins the election (most up-to-date data takes priority).
- Ties are broken by node ID lexicographic order (deterministic, no randomness).
- A node votes once per term (first-come-first-served), preventing split votes.
- Majority (2 out of 3) required to win. This means one node can be down and election still succeeds.
- Elected node transitions to primary and begins accepting writes; losers remain secondaries and connect to the new primary's replication stream.

**No external coordination service** — election is peer-to-peer via gRPC. This keeps the system self-contained and educational.

### New Components

| Component | Location | Purpose |
|---|---|---|
| `ClusterOptions` | `ToyDb/Services/ClusterOptions.cs` | Node identity, partition ID, peer addresses, election config |
| `ElectionService` | `ToyDb/Services/ElectionService.cs` | Background service: failure detection, vote requesting, term management, role transitions |
| `ClusterService` | `ToyDb/Services/ClusterService.cs` | gRPC service implementing inter-node election RPCs (RequestVote, Heartbeat) |
| `election.proto` | `ToyDbContracts/Protos/election.proto` | Proto definitions for RequestVote, VoteResponse, Heartbeat |
| `ReplicaState` | `ToyDb/Services/ReplicaState.cs` | Runtime singleton tracking current role, term, leader address |

### Election Flow

```
Secondary detects primary failure (replication stream disconnect or health probe timeout)
  → Waits ElectionTimeoutMs (5s default) with random jitter (±500ms)
  → Increments local term
  → Sends RequestVote(term, lastLsn, nodeId) to all peers in partition
  → Needs majority (2/3) votes to win
  → If elected: updates ReplicaState to Primary, logs term transition to WAL
  → If not elected: resets election timer, waits for new primary's replication stream
  → Other secondaries detect new primary via heartbeat and reconnect replication stream
```

### Failure Detection

Two complementary mechanisms:
1. **Replication stream disconnect** — secondary notices the stream from primary drops. Fastest signal (~immediate).
2. **Health probe failure** — routing layer's existing `HealthProbeService` marks primary as `NotServing`. Secondary can also independently probe primary.

### Write Gating

`ClientService.SetValue` and `DeleteValue` check `ReplicaState.Role` before accepting writes. If not primary, return `FAILED_PRECONDITION` so the routing layer can retry against the actual primary.

## Implementation Steps

### Step 1: Extend Node Configuration for Cluster Awareness
- Create `ClusterOptions` with: `NodeId`, `PartitionId`, `PeerAddresses`, `ElectionTimeoutMs`, `HeartbeatIntervalMs`
- Extend `ReplicaOptions` to include `NodeId` and `PartitionId`
- Add two new replicas (`toydb-p1-r3`, `toydb-p2-r3`) to docker-compose
- Update `docker-compose.override.yml` to pass cluster env vars to all replicas
- Update `ServiceRegistrationExtensions` to bind `ClusterOptions`

**Files:**
- New: `ToyDb/Services/ClusterOptions.cs`
- `ToyDb/Services/ReplicaOptions.cs:3` — add `NodeId`, `PartitionId`
- `docker-compose.yml` — add `toydb-p1-r3` and `toydb-p2-r3` services
- `docker-compose.override.yml` — add `Cluster__*` env vars to all 6 replicas
- `ToyDb/Extensions/ServiceRegistrationExtensions.cs:31` — register `ClusterOptions`

### Step 2: Define Election gRPC Protocol
- Create `election.proto` with `RequestVote`, `VoteResponse`, and `Heartbeat` RPCs
- Include `term`, `last_lsn`, `node_id` in vote requests
- Include `term`, `leader_id`, `commit_lsn` in heartbeats

**Files:**
- New: `ToyDbContracts/Protos/election.proto`
- `ToyDbContracts/ToyDbContracts.csproj` — ensure proto is compiled

### Step 3: Implement Inter-Node Cluster Service
- Create `ClusterService` implementing election RPCs
- `RequestVote`: validate term, check if already voted for this term, compare LSN, grant or deny vote
- `Heartbeat`: update last-heard-from timestamp, reset election timer, update known leader
- Persist voted-for term and leader state in WAL metadata or in-memory (with WAL checkpoint)

**Files:**
- New: `ToyDb/Services/ClusterService.cs`
- `ToyDb/Program.cs:16` — map `ClusterService`

### Step 4: Implement Election Service
- Create `ElectionService` as `BackgroundService`
- Monitor primary health via replication stream status or direct probing
- On primary failure detection:
  - Wait `ElectionTimeoutMs` with random jitter
  - Increment term, vote for self
  - Send `RequestVote` to all peers in parallel
  - If majority (2/3): transition to primary, log term change, start accepting writes
  - If not: step back, reset timer
- On receiving higher term in heartbeat: update local term, become follower
- On receiving heartbeat from new leader: update `ReplicaState`, reconnect replication stream

**Files:**
- New: `ToyDb/Services/ElectionService.cs`
- `ToyDb/Extensions/ServiceRegistrationExtensions.cs:31` — register as hosted service

### Step 5: Dynamic Role Transitions
- Create `ReplicaState` singleton that tracks current role, term, and leader address
- Update `ClientService` to check `ReplicaState.Role` before accepting writes
- Update `SecondaryCatchUpService` to reconnect to new primary on leader change
- Update `WriteStorageService` to be role-aware

**Files:**
- New: `ToyDb/Services/ReplicaState.cs`
- `ToyDb/Services/ClientService.cs:58,81` — role-aware write gating
- `ToyDb/Services/SecondaryCatchUp/SecondaryCatchUpService.cs` — reconnection on leader change

### Step 6: Integration Tests
- Test: primary failure → secondary election → writes resume
- Test: split vote resolution (higher LSN wins)
- Test: stale term rejection (node with old term cannot become primary)
- Test: node restart recovers correct term state

**Files:**
- New test files in `ToyDb.Tests.Integration/`

## Deferred to Follow-Up (`plan-phase3b-routing-redundancy.md`)
- Step 6 (original): Routing layer dynamic primary discovery
- Step 7 (original): Routing layer redundancy (multiple routing instances, client failover)

## Verification & Testing

### Manual Verification
1. Start full stack: `./run.sh`
2. Write test data via routing client
3. Kill primary container for partition 1: `docker stop toydb-p1-r1`
4. Verify a secondary promotes itself to primary within ~5-10 seconds
5. Verify writes succeed on partition 1 after promotion
6. Restart old primary: verify it rejoins as secondary and catches up

### Automated Tests
- Unit tests for election logic (term comparison, vote counting, LSN priority)
- Integration tests for full election cycle with Docker Compose

### Success Criteria
- Primary failure is detected within 5 seconds
- New primary elected within 10 seconds
- No data loss during failover (all committed writes are preserved)
- Writes resume automatically without client code changes (once routing picks up new primary)

## References
- `plans/review-replication.md` — Phase 3 recommendations
- Raft paper (inspiration for term-based election): https://raft.github.io/raft.pdf
- Existing health check: `ToyDbRouting/Services/HealthProbeService.cs`
- Existing replication streaming: `ToyDb/Services/ClientService.cs:92`
- Existing consistent hashing: `ToyDbRouting/Services/ConsistentHashRing.cs`
