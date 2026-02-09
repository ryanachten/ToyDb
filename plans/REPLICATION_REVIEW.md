# ToyDb Replication Review

> **Date:** February 9, 2026
> **Scope:** Full review of the replication architecture, current capabilities, gaps, and recommended next steps.

---

## 1. Current Topology

The system runs **5 containers** via Docker Compose:

| Container | Role | Ports (HTTP/HTTPS) |
|---|---|---|
| `toydb-routing` | Routing / coordination layer | 8080 / 8081 |
| `toydb-p1-r1` | Partition 1, Replica 1 (**Primary**) | 8082 / 8083 |
| `toydb-p1-r2` | Partition 1, Replica 2 (**Secondary**) | 8084 / 8085 |
| `toydb-p2-r1` | Partition 2, Replica 1 (**Primary**) | 8086 / 8087 |
| `toydb-p2-r2` | Partition 2, Replica 2 (**Secondary**) | 8088 / 8089 |

**Summary: 2 partitions × 2 replicas = 4 data nodes + 1 routing node.**

Each partition has a statically-configured primary and a single secondary. Node identity is set via the `NODE_NAME` environment variable, which isolates on-disk WAL and data store directories.

---

## 2. Architecture Overview

```
┌─────────────────┐
│   ToyDbClient    │   (CLI, gRPC via Routing.proto)
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────┐
│           ToyDbRouting                   │
│                                          │
│  • xxHash32 partition selection           │
│  • NTP-based timestamp injection          │
│  • Fan-out writes to all replicas         │
│  • Random-secondary read routing          │
└──────┬──────────────┬───────────────────┘
       │              │   (Data.proto, gRPC)
  ┌────┴────┐    ┌────┴────┐
  │ P1 (R1) │    │ P2 (R1) │   ← Primaries
  │ P1 (R2) │    │ P2 (R2) │   ← Secondaries
  └─────────┘    └─────────┘

Each ToyDb node:
┌──────────────────────────────┐
│  ClientService (gRPC)         │
│    ├─ ReadStorageService      │
│    │   ├─ KeyEntryCache (LRU) │
│    │   ├─ KeyOffsetCache      │
│    │   └─ DataStoreRepository │
│    └─ WriteStorageService     │
│        ├─ ConcurrentQueue     │
│        ├─ WAL Repository      │
│        └─ DataStoreRepository │
│  LogCompactionProcess (bg)    │
└──────────────────────────────┘
```

---

## 3. gRPC Service Contracts

Two proto files define two separate gRPC services:

- **`Routing.proto`** — Client-facing. Exposed only by the routing node. `SetValue` / `DeleteValue` requests do **not** carry a timestamp; the routing layer injects one.
- **`Data.proto`** — Internal node-level. Every database node exposes this. Requests include an NTP-assigned `Timestamp`.

The routing layer uses AutoMapper to translate between the two message types and injects the NTP timestamp during the mapping.

---

## 4. How Replication Currently Works

### 4.1 Write Path

1. Client sends `SetValue` to **RoutingService** (no timestamp).
2. Routing assigns an NTP-corrected timestamp.
3. Routing computes `xxHash32(key) % partitionCount` → selects partition.
4. Routing sends the write **simultaneously** to:
   - The partition's **primary** replica.
   - **All secondary** replicas.
5. Routing awaits `Task.WhenAll(primaryTask, secondaryThresholdTask)`.

**Key detail:** The primary does **not** propagate writes to secondaries. The routing layer acts as the sole replication coordinator, sending the identical request to every replica. This is "routing-coordinated multi-write" rather than true leader-based replication.

### 4.2 Read Path

1. Client sends `GetValue` to **RoutingService**.
2. Routing computes the partition via hash.
3. Routing selects a **random secondary** replica (`Partition.GetReadReplica()`).
4. Routing forwards the read to that single secondary.

**Reads never hit the primary.** This is a deliberate choice to offload the write leader, but it means every read is subject to replication lag.

### 4.3 Delete Path

Identical to the write path — a tombstone (`NullMarker`) is appended to WAL + data store on all replicas.

### 4.4 Write Threshold Mechanism

The `WhenThresholdCompleted` extension method provides a quorum-like knob:

| `completedSecondaryWritesThreshold` | Behavior |
|---|---|
| `0` | Fire-and-forget all secondary writes (async, no durability guarantee) |
| `N > 0` | Wait for `N` secondaries to ACK; remaining continue in background |
| `null` (default) | Wait for **all** secondaries (strongest guarantee, current default) |

Currently the threshold is **not configured**, so it defaults to waiting for all secondaries (1 in the current setup).

---

## 5. What Is Missing

### 5.1 No Catch-Up / Anti-Entropy

If a secondary is down during a write, that write is **permanently lost** for that replica. There is no:
- Write-ahead log shipping from primary to secondary
- Merkle tree comparison
- Gossip-based anti-entropy protocol
- Replica sync on startup or recovery

### 5.2 No Failover or Leader Election

- Primary/secondary assignments are **static** (hardcoded in routing config).
- If a primary goes down, writes to that partition fail entirely.
- There is no Raft, Paxos, or any consensus protocol.
- No health checking of replica availability.

### 5.3 No Read Consistency Guarantees

| Guarantee | Status |
|---|---|
| Read-after-write (same client) | ❌ Not guaranteed — reads go to random secondary |
| Monotonic reads | ❌ Not guaranteed — random replica per request |
| Consistent prefix | ❌ Not guaranteed |
| Linearizability | ❌ Not supported |

### 5.4 No Conflict Detection or Resolution

- If a write to one replica succeeds and another fails, no rollback or retry occurs.
- The `WhenThresholdCompleted` method has `// TODO: handle failure cases` comments.
- No vector clocks, version numbers, or last-write-wins beyond the NTP timestamp.

### 5.5 Routing Layer Is a Single Point of Failure

The routing node is the only entry point. If it goes down, the system is completely unavailable.

### 5.6 Nodes Are Unaware of the Cluster

Individual ToyDb nodes have **no knowledge** of:
- Other replicas in their partition
- Whether they are a primary or secondary
- The cluster topology at all

All replication intelligence lives in the routing layer.

---

## 6. Existing TODOs in Code

| Location | TODO |
|---|---|
| `RoutingService.SetValue` | `// TODO: handle partital writes, node outages, etc` |
| `RoutingService.DeleteValue` | `// TODO: handle partital writes, node outages, etc` |
| `Partition.GetReadReplica` | `// TODO: should we be selecting random secondaries? Probably not, we might need to check for data consistency etc if we're doing eventual consistency` |
| `TaskExtensions.WhenThresholdCompleted` | `// TODO: handle failure cases` |
| `TaskExtensions.FireAndForget` | `// TODO: handle failure cases` |
| `KeyOffsetCache` | `// TODO: currently all keys/offsets need to be kept in memory so no cache size limit has been set. This feels like a scalability issue` |
| `KeyEntryOptions` | `// TODO: This treats all entries the same, which isn't optimal. Really we want to take the data size into consideration` |

---

## 7. Recommended Next Steps

### Phase 1: Resilience & Error Handling (Foundation)

1. **Handle partial write failures in `RoutingService`**
   - Detect when a replica write fails and log/record the failure.
   - Return a response that indicates partial success (e.g., "wrote to 1 of 2 replicas").
   - Implement retry logic with exponential backoff for failed replica writes.

2. **Add health checks for replicas**
   - Implement a gRPC health-check endpoint on each node (standard `grpc.health.v1`).
   - Have the routing layer periodically probe each replica and track availability.
   - Mark unhealthy replicas as unavailable; skip them in read routing.

3. **Handle `WhenThresholdCompleted` failure cases**
   - Currently the TODO says "handle failure cases" — at minimum, log failures and expose metrics.
   - Consider a dead-letter queue of failed writes that can be retried.

### Phase 2: Replica Catch-Up & Consistency

4. **Implement a replication log / changelog on the primary**
   - Assign a monotonically-increasing **Log Sequence Number (LSN)** to each write on the primary.
   - Expose a gRPC streaming endpoint: `StreamReplicationLog(fromLsn)` that secondaries can consume.

5. **Add secondary catch-up on startup**
   - When a secondary starts, it should compare its latest LSN with the primary's.
   - Pull any missing entries via the replication log stream.
   - This resolves the "missed writes while down" problem.

6. **Improve read consistency**
   - **Option A (Read-from-primary):** Allow the routing layer to direct reads to the primary when strong consistency is needed (configurable per request).
   - **Option B (Read-your-writes):** Track the latest write LSN per client session, and only route reads to replicas that are caught up to that LSN.
   - **Option C (Quorum reads):** Read from multiple replicas and return the latest timestamp.

### Phase 3: Failover & High Availability

7. **Introduce node self-awareness**
   - Pass role (primary/secondary) and partition info to each node via configuration.
   - Nodes should know their identity and their peers.

8. **Implement leader election**
   - When the primary is unreachable, secondaries should be able to elect a new leader.
   - Start with a simple approach (e.g., the routing layer promotes a secondary after a timeout).
   - Long-term: implement Raft consensus for partition-level leader election.

9. **Routing layer redundancy**
   - Deploy multiple routing instances behind a load balancer.
   - They can share the same static partition map initially.
   - Long-term: routing instances can use gossip or a shared config store (etcd/ZooKeeper) for partition map consistency.

### Phase 4: Advanced Replication Features

10. **Configurable consistency levels per request**
    - Allow clients to specify: `ONE`, `QUORUM`, `ALL` for both reads and writes.
    - Map these to the existing threshold mechanism for writes, and add multi-replica reads for read quorums.

11. **Conflict resolution**
    - Introduce version vectors or Lamport clocks alongside/replacing NTP timestamps.
    - Implement last-write-wins (LWW) resolution at the storage level.
    - Consider read-repair: when a read detects stale data on a replica, push the latest value.

12. **Anti-entropy background process**
    - Periodically compare Merkle trees of key hashes between replicas in the same partition.
    - Sync any divergent entries.

---

## 8. Summary

The current replication implementation establishes a solid **structural foundation** — the routing layer, partition mapping, primary/secondary topology, and threshold-based write acknowledgment are all in place. However, the system currently provides **no guarantees** that replicas stay in sync after failures, offers **no failover** capability, and has **no read consistency** guarantees. The recommended phases above build incrementally from error handling → catch-up replication → failover → advanced consistency, aligned with the project's goal of exploring concepts from *Designing Data-Intensive Applications*.
