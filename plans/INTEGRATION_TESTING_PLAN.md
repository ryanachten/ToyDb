# Integration Testing Plan

> **Date:** February 9, 2026
> **Status:** Planning
> **Current Coverage:** BasicCrudTests.cs

---

## Current Test Coverage

✅ **BasicCrudTests.cs**
- Single-key CRUD operations
- Special values (empty, large, unicode)
- Get all values
- Sequential consistency

---

## Critical User Flows to Test

### 1. Partitioning & Routing
**Goal:** Verify data is correctly distributed across partitions

- **Key Distribution Test**
  - Write multiple keys and verify they route to different partitions
  - Validate xxHash32 routing is deterministic
  - Ensure same key always routes to same partition

- **Cross-Partition Operations**
  - GetAllValues returns data from all partitions
  - Verify partition isolation (keys don't leak across partitions)

### 2. Replication Consistency
**Goal:** Ensure replicas maintain consistency

- **Write Propagation**
  - Write to primary, verify secondary has same data
  - Test fan-out write to all replicas succeeds
  - Validate timestamp consistency across replicas

- **Read-After-Write Consistency**
  - Write a key, immediately read from different replica
  - Verify no stale reads within expected timeframe

- **Update Consistency**
  - Update a key multiple times rapidly
  - Verify all replicas converge to same final value
  - Test delete propagation across replicas

### 3. Concurrent Operations
**Goal:** Verify system handles concurrent client access

- **Concurrent Writes to Different Keys**
  - Multiple clients writing different keys simultaneously
  - Verify all writes succeed and no data loss

- **Concurrent Writes to Same Key**
  - Multiple clients updating same key
  - Verify final state is one of the written values (last-write-wins)

- **Mixed Concurrent Operations**
  - Simultaneous reads, writes, deletes on overlapping key sets
  - Verify no deadlocks or inconsistencies

### 4. Error Handling & Resilience
**Goal:** System behaves correctly under failure conditions

- **Network Timeouts**
  - Simulate slow/timeout responses
  - Verify appropriate error handling

- **Invalid Operations**
  - Invalid key formats
  - Oversized values
  - Verify graceful error responses

- **Partial Replica Failure** (future)
  - One replica down, operations still succeed
  - System degrades gracefully

### 5. Data Persistence & Recovery
**Goal:** Data survives restarts

- **Restart Persistence**
  - Write data, restart all nodes, verify data intact
  - Test WAL recovery on startup

- **Partition-Specific Persistence**
  - Verify each partition maintains independent storage
  - Restart single partition, verify no data cross-contamination

### 6. Performance & Scale
**Goal:** System handles realistic workloads

- **Bulk Operations**
  - Write 1000+ keys rapidly
  - Verify all persisted correctly
  - Measure throughput baseline

- **Large Value Handling**
  - Test values at various sizes (1KB, 100KB, 1MB)
  - Verify performance degradation is acceptable

- **High Concurrency**
  - 10+ concurrent clients
  - Mixed read/write workload
  - Verify no resource exhaustion

### 7. Cache Behavior
**Goal:** Verify caching layer works correctly

- **Cache Hit/Miss Patterns**
  - Write data, verify cached
  - Delete data, verify cache invalidation
  - Test cache consistency with underlying storage

- **Cache Warming After Restart**
  - Restart node, verify cache rebuilds correctly

---

## Test Organization

### Recommended Test Classes

```
Tests/
├── BasicCrudTests.cs          ✅ (existing)
├── PartitioningTests.cs       🔲 (routing & distribution)
├── ReplicationTests.cs        🔲 (consistency across replicas)
├── ConcurrencyTests.cs        🔲 (parallel operations)
├── ErrorHandlingTests.cs      🔲 (failure scenarios)
├── PersistenceTests.cs        🔲 (restart & recovery)
└── PerformanceTests.cs        🔲 (load & scale)
```

---

## Priority Ranking

### High Priority (Next Sprint)
1. **ReplicationTests** - Core distributed system behavior
2. **PartitioningTests** - Validate routing logic
3. **ConcurrencyTests** - Critical for multi-client scenarios

### Medium Priority
4. **PersistenceTests** - Important but slower to execute
5. **ErrorHandlingTests** - Defensive coverage

### Lower Priority
6. **PerformanceTests** - Baseline metrics, not blocking

---

## Test Infrastructure Needs

- **Test Helpers** (expand existing)
  - Multi-client orchestration
  - Container restart utilities
  - Partition/replica inspection tools
  
- **Test Data Generators** (existing TestDataGenerator is good start)
  - Bulk key generation
  - Deterministic random values for reproducibility

- **Assertions**
  - Eventually consistent assertions (retry with timeout)
  - Cross-replica comparison utilities

---

## Notes

- Focus on **user-observable behavior** rather than internal implementation
- Tests should be **deterministic** and **repeatable**
- Keep test execution time reasonable (< 5 min for full suite initially)
- Consider separate test categories for slow tests (persistence, performance)
