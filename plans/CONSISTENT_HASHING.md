# Plan: Consistent Hashing for Routing Service

## Objective
Replace the current modulo-based partitioning logic in `RoutingService` with consistent hashing to improve scalability and data distribution stability when adding or removing nodes.

## Rationale
The current implementation `XxHash32(key) % _partitions.Length` causes a massive data reshuffle whenever the number of partitions changes. Consistent hashing minimizes this by ensuring that adding or removing a node only affects a small fraction of the keys (roughly `1/n` keys).

## Architecture

### 1. Consistent Hash Ring
A ring-like data structure that maps nodes (partitions) and keys to a large 32-bit hash space.

### 2. Virtual Nodes
To ensure a more uniform distribution of keys across partitions, each physical partition will be mapped to multiple points (virtual nodes) on the ring. This prevents "hot spots" where one node handles significantly more traffic than others.

### 3. Algorithm
- **Hashing:** Use `XxHash32` (consistent with current implementation) to hash both virtual nodes and keys.
- **Storage:** Store virtual nodes in a `SortedDictionary<uint, Partition>` where the key is the hash value.
- **Lookup:** To find a partition for a key:
  1. Compute the hash of the key.
  2. Find the first virtual node with a hash value greater than or equal to the key's hash.
  3. If no such node exists, wrap around to the first node in the sorted dictionary (completing the ring).

## Implementation Steps

### 1. Update `RoutingOptions`
Add `VirtualNodesPerPartition` to `ToyDbRouting/Models/RoutingOptions.cs`.
- **Default:** 200 virtual nodes per partition is a good starting point for balanced distribution.

### 2. Define `ConsistentHashRing` Class
Create `ToyDbRouting/Services/ConsistentHashRing.cs` to encapsulate the ring logic.
- **Properties:**
  - `SortedDictionary<uint, Partition> _ring`: Stores virtual nodes.
  - `int _virtualNodeCount`: Number of virtual nodes per partition.
- **Methods:**
  - `AddPartition(Partition partition)`: Generates `_virtualNodeCount` hashes for the partition (e.g., using `partitionId + ":" + index`) and adds them to the ring.
  - `GetPartition(string key)`: Performs the binary search on `_ring` to find the responsible partition.

### 3. Refactor `RoutingService`
- Replace `Partition[] _partitions` with an instance of `ConsistentHashRing`.
- Initialize the ring in the constructor by iterating through `routingOptions.Value.Partitions`.
- Update `GetPartition(string key)` to delegate to the `ConsistentHashRing`.
- Update `GetAllValues` to iterate over unique partitions in the ring.

### 4. Validation & Testing
- **Unit Tests:** Create `ToyDbUnitTests/Services/ConsistentHashRingTests.cs` to verify:
  - Even distribution of keys across partitions.
  - Minimal reshuffling when adding or removing partitions.
  - Correct wrap-around behavior.
- **Integration Tests:** Run existing `BasicCrudTests` and `PartitioningTests` to ensure no regressions in basic functionality.
