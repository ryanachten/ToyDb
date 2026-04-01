# Plan: Add Health Checks for Replicas

## Objective
Implement health checks for all ToyDb replica nodes to improve routing reliability and availability. This includes exposing a standard gRPC health endpoint on each node, enabling the routing layer to probe replica health, and updating routing logic to avoid unhealthy replicas.

## Context & Background
The current system lacks awareness of replica health, leading to routing failures if a replica is down. Implementing standard gRPC health checks allows the routing layer to proactively skip unhealthy nodes.

## Architecture & Design

### 1. gRPC Health Protocol
Each ToyDb node will expose the standard `grpc.health.v1.Health` service.

### 2. Probing & Tracking
The routing layer will implement a background process to periodically probe replicas and maintain an in-memory status map.

### 3. Health-Aware Routing
Routing logic for both reads and writes will filter out replicas marked as unhealthy.

## Implementation Steps

1. **Implement gRPC Health Endpoint on Each Node**:
    - Add the standard `grpc.health.v1.Health` service to each ToyDb node (primary and secondary).
    - Implement the `Check` and (optionally) `Watch` methods to report node health status (e.g., `SERVING`, `NOT_SERVING`).
    - Integrate health status with node readiness (e.g., WAL available, storage initialized).
    - Register the health service in the node's gRPC server startup.
2. **Routing Layer Health Probing**:
    - Add a background health-check process in the routing service.
    - Periodically call the gRPC health endpoint of each replica (configurable interval, e.g., every 2–5 seconds).
    - Track the health status of each replica in memory (e.g., a concurrent dictionary of node name → health state).
    - Log health check failures and recoveries for observability.
3. **Routing Logic Update**:
    - Update read routing logic to exclude replicas marked as unhealthy.
    - If all replicas in a partition are unhealthy, return an error to the client.
    - Optionally, expose replica health status via a diagnostic endpoint or admin API.

## Verification & Testing
- Unit test the health endpoint implementation (healthy/unhealthy transitions).
- Integration test: simulate node failures and verify routing skips unhealthy replicas.
- Add tests for recovery (node returns to healthy state and is used again).

## References
- [gRPC Health Checking Protocol](https://github.com/grpc/grpc/blob/master/doc/health-checking.md)
- [Grpc.HealthCheck NuGet package](https://www.nuget.org/packages/Grpc.HealthCheck)
