# Review: ToyDb Replication

## Overview
Full review of the ToyDb replication architecture, current capabilities, gaps, and recommended next steps as of February 2026.

## Current State

### Topology
The system runs 2 partitions with 2 replicas each (1 primary, 1 secondary), plus a single routing node. Node roles are statically assigned via environment variables.

### Architecture
- **Routing-Coordinated Multi-Write**: The routing layer fanned out writes to all replicas simultaneously.
- **Read Routing**: Reads are directed to random secondaries, offloading the primary.
- **gRPC Protocols**: `Routing.proto` for clients, `Data.proto` for internal replica communication.

## Gaps & Issues
- **No Catch-Up / Anti-Entropy**: Missed writes on a secondary during downtime are permanently lost.
- **No Failover / Election**: Primary/secondary roles are static; failure of a primary breaks writes for that partition.
- **Inconsistent Reads**: No read-after-write or monotonic read guarantees due to random secondary selection.
- **Single Point of Failure**: The routing node is the only entry point.
- **Node Isolation**: Nodes are unaware of the cluster topology or their peers.

## Recommendations

### Phase 1: Resilience & Foundation
- Handle partial write failures in `RoutingService` with retries and logging.
- Implement standard gRPC health checks.
- Address failed write cases in threshold mechanism.

### Phase 2: Consistency & Catch-Up
- Implement LSN-based replication logs on the primary.
- Add secondary catch-up on startup to pull missing entries.
- Improve read consistency (e.g., read-your-writes).

### Phase 3: High Availability
- Introduce node self-awareness.
- Implement leader election/promotion.
- Add routing layer redundancy.

### Phase 4: Advanced Features
- Configurable consistency levels (ONE, QUORUM, ALL).
- Conflict resolution (e.g., version vectors).
- Anti-entropy background processes.
