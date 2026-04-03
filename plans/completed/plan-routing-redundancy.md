# Plan: Phase 4 — Routing Redundancy

## Scope
This plan covers **Steps 6–7 from the original replication review**, which were deferred from `plan-leader-election.md`. It addresses dynamic primary discovery and routing layer redundancy.

## Objective
Enable the routing layer to automatically discover the current primary for each partition after leader election, and provide routing layer redundancy for high availability.

## Context & Background
The leader election plan (`plans/completed/plan-leader-election.md`) introduced `ReplicaState` to track the current role (Primary/Secondary) and leader address. However, the routing layer currently uses static `PrimaryReplicaAddress` from configuration and never updates when a new primary is elected.

If a primary fails and a secondary promotes, the routing layer continues sending writes to the old (now failed) primary until configuration is manually updated.

Phase 1 (resilience/retries), Phase 2 (LSN catch-up), and Phase 3 (leader election) are complete.

## Architecture & Design

### Design Decisions

**Dynamic Primary Discovery via ReplicaState**
- The routing layer will query each node's gRPC service to discover its current role
- A new `ClusterClient` in the routing layer communicates with ToyDb nodes
- `ReplicaState` should expose a gRPC endpoint (or reuse `ClusterService`) that returns the node's current role, term, and whether it is the leader
- The routing layer polls periodically or subscribes to leadership changes

**No External Service Discovery**
- Avoid introducing Consul, etcd, or ZooKeeper — keep the system self-contained
- Use the existing `ClusterService` gRPC endpoints to query node state

**Routing Layer Redundancy**
- Multiple routing instances can run behind a load balancer
- Clients connect to the load balancer, not individual routers
- Router instances share state via the consistent hash ring (stateless)
- Health checks ensure unhealthy routers are removed from the pool

### New Components

| Component | Location | Purpose |
|---|---|---|
| `ClusterClient` | `ToyDbRouting/Clients/ClusterClient.cs` | gRPC client to query node role/state |
| `PartitionManager` | `ToyDbRouting/Services/PartitionManager.cs` | Monitors partition primaries, updates `Partition` with current leader |
| `RoutingOptions` extension | `ToyDbRouting/Models/RoutingOptions.cs` | Add discovery interval config |
| `Partition` update | `ToyDbRouting/Models/Partition.cs` | Track dynamic primary instead of static |

### Primary Discovery Flow

```text
RoutingService starts
  → PartitionManager initializes
    → For each partition, query all replicas for their role
    → Determine current primary from responses
    → Update Partition.PrimaryReplica dynamically
  → On write failure to primary:
    → Re-query replicas to discover new primary
    → Update Partition.PrimaryReplica
  → Background refresh every DiscoveryIntervalSeconds (default 10s)
```

### Routing Redundancy Flow

```text
Client connects to load balancer (e.g., nginx)
  → Load balancer forwards to healthy router instance
  → Router processes request using ConsistentHashRing
    → PartitionManager provides current primary per partition
  → If router becomes unhealthy:
    → Health check marks it unhealthy
    → Load balancer removes it from pool
  → Client transparently fails over to another router
```

## Implementation Steps

### Step 0: Add GetRole RPC to Election Service (Prerequisite)
- Extend `election.proto` with `GetRoleRequest` and `GetRoleResponse` messages
- `GetRoleRequest`: empty (no parameters needed)
- `GetRoleResponse`: `role` (Primary/Secondary), `term`, `leader_id`, `leader_address`
- Add `GetRole` RPC to the Election service
- Implement `GetRole` in `ClusterService` to return current role from `ReplicaState`

**Files:**
- `ToyDbContracts/Protos/election.proto` — add GetRole messages and RPC
- `ToyDb/Services/ClusterService.cs` — implement GetRole handler

### Step 1: Extend ClusterClient for Role Queries
- Create `ClusterClient` with methods to query `GetRole`, `GetTerm`, `GetLeaderAddress`
- Reuse or extend existing proto definitions from `ToyDbContracts`

**Files:**
- New: `ToyDbRouting/Clients/ClusterClient.cs`
- Reference: `ToyDbContracts/Protos/election.proto` (or create routing variant)

### Step 2: Create PartitionManager Service
- Create `PartitionManager` as a hosted service that monitors partition primaries
- On initialization: query all replicas to discover current primary per partition
- On schedule: refresh primary assignments
- On write failure: trigger immediate re-discovery for that partition

**Files:**
- New: `ToyDbRouting/Services/PartitionManager.cs`
- `ToyDbRouting/Program.cs` — register service

### Step 3: Update Partition Model for Dynamic Primary
- Modify `Partition` to track current primary dynamically
- Replace static `PrimaryReplica` with property that resolves to current leader
- Cache the current primary, update via `PartitionManager`

**Files:**
- `ToyDbRouting/Models/Partition.cs:11` — make primary dynamic

### Step 4: Integrate with RoutingService
- `RoutingService` uses `PartitionManager` to get current primary
- On `FAILED_PRECONDITION` from primary, trigger re-discovery
- Add retry logic for transient primary failures

**Files:**
- `ToyDbRouting/Services/RoutingService.cs:80` — handle `FAILED_PRECONDITION`

### Step 5: Add Routing Redundancy Config
- Extend `RoutingOptions` with `RouterInstanceId`, discovery intervals
- Document deployment pattern for multiple router instances behind load balancer
- Add health check endpoint for router instances

**Files:**
- `ToyDbRouting/Models/RoutingOptions.cs` — add redundancy config
- `ToyDbRouting/Program.cs` — add health check endpoint

### Step 6: Integration Tests
- Test: primary fails → election → routing discovers new primary → writes succeed
- Test: multiple router instances behind load balancer
- Test: router instance failure → load balancer removes it → client fails over

**Files:**
- New test files in `ToyDbRouting.Tests.Integration/`

## Verification & Testing

### Manual Verification
1. Start full stack: `./run.sh`
2. Write test data via routing client
3. Kill primary container: `docker stop toydb-p1-r1`
4. Wait for election (~5-10s) and routing discovery (~10s)
5. Verify writes succeed on partition 1 after promotion (routing auto-discovers new primary)
6. Verify old primary rejoins as secondary and catches up

### Automated Tests
- Unit tests for primary discovery logic
- Integration tests for full failover cycle with routing discovery
- Load balancer + multi-router tests

### Success Criteria
- Primary failure detected and new primary discovered within 15 seconds
- Writes resume automatically without manual intervention
- Multiple router instances provide redundancy
- No data loss during failover (all committed writes preserved)

## Dependencies
- Requires `plan-leader-election.md` completion (provides `ReplicaState` and `ClusterService`)
- Requires `ToyDbContracts` proto updates for role query RPCs

## References
- `plans/completed/plan-leader-election.md` — Phase 3 (leader election)
- `plans/review-replication.md` — original review with Steps 6-7
- `ToyDbRouting/Services/RoutingService.cs` — current routing implementation
- `ToyDbRouting/Models/Partition.cs` — partition model
- `ToyDb/Services/ReplicaState.cs` — runtime role tracking (from Phase 3)
