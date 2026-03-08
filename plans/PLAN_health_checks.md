# Plan: Add Health Checks for Replicas

## Objective
Implement health checks for all ToyDb replica nodes to improve routing reliability and availability. This includes exposing a standard gRPC health endpoint on each node, enabling the routing layer to probe replica health, and updating routing logic to avoid unhealthy replicas.

---

## Steps

### 1. Implement gRPC Health Endpoint on Each Node
- Add the standard `grpc.health.v1.Health` service to each ToyDb node (primary and secondary).
- Implement the `Check` and (optionally) `Watch` methods to report node health status (e.g., `SERVING`, `NOT_SERVING`).
- Integrate health status with node readiness (e.g., WAL available, storage initialized).
- Register the health service in the node's gRPC server startup.

### 2. Routing Layer: Health Probing
- Add a background health-check process in the routing service.
- Periodically call the gRPC health endpoint of each replica (configurable interval, e.g., every 2–5 seconds).
- Track the health status of each replica in memory (e.g., a concurrent dictionary of node name → health state).
- Log health check failures and recoveries for observability.

### 3. Routing Logic: Skip Unhealthy Replicas
- Update read routing logic to exclude replicas marked as unhealthy.
- If all replicas in a partition are unhealthy, return an error to the client.
- Optionally, expose replica health status via a diagnostic endpoint or admin API.

### 4. Testing
- Unit test the health endpoint implementation (healthy/unhealthy transitions).
- Integration test: simulate node failures and verify routing skips unhealthy replicas.
- Add tests for recovery (node returns to healthy state and is used again).

### 5. Documentation
- Document the health check protocol and configuration options.
- Update operational runbooks to include health check troubleshooting.

---

## Deliverables
- gRPC health endpoint on all ToyDb nodes
- Routing layer health probe and in-memory health state
- Updated routing logic to skip unhealthy replicas
- Tests and documentation

---

## References
- [gRPC Health Checking Protocol](https://github.com/grpc/grpc/blob/master/doc/health-checking.md)
- [Grpc.HealthCheck NuGet package](https://www.nuget.org/packages/Grpc.HealthCheck)
