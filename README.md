# ToyDb

Toy database for learning the fundamentals on database implementation and data management.

## Database structure

The database is currently a simple key-value store which receives commands using gRPC.

- [ToyDb](./ToyDb/) - Database server source code
- [ToyDbClient](./ToyDbClient/) - Database client source code
- [ToyDbContracts](./ToyDbContracts/) - Definitions of the protobuf messages accepted by the database

## Learning objectives

The learning outcomes from this work are heavily inspired by different chapters from [Designing Data Intensive Applications](https://www.amazon.com.au/Designing-Data-Intensive-Applications-Reliable-Maintainable/dp/1449373321) by Martin Kleppmann.

The current capabilities we aim to explore are:

- Data storage and retrieval
- Data encoding
- Partitioning
- Replication
- Transactions
- Distributed hosting

## Usage

### Prerequisites

- Generate HTTPS certificate:
  - `dotnet dev-certs https -ep "$env:USERPROFILE\.aspnet\https\aspnetapp.pfx"  -p password -t`

### Via client

- Run the server via `dotnet run --project .\ToyDb\ToyDb.csproj -lp https`
  - **Note:** must be run via HTTPS for Protobuf communication to work
- Run the client via:
  - Get value: `dotnet run --project .\ToyDbClient\ToyDbClient.csproj -- get Hello`
  - Set value: `dotnet run --project .\ToyDbClient\ToyDbClient.csproj -- set Hello=World`

## Design decisions

### Protocol

- ToyDb uses Google Remote Procedure Calls (gRPC) to communicate between clients and the database with messages defined using Protobufs. This allows for efficient communication, however, this could be problematic if we need to support .NET AoT in the future (not sure there's AoT gRPC support).

### Encoding

- ToyDb uses binary encoding of data to when saving to disk for efficient writes and reads. Values are currently Base64 encoded for no specific reason. This it might be something we want to remove in the future.

### Storage

- When writing to disk, ToyDb stores keys and their values to two places; a Write-Ahead Log, used for auditing and potentially in the future data recovery if needed, as well as an active Append-only Log (AoL). The active log is used for reads and goes through a compaction process to remove data redundancies at regular intervals to ensure reads remain efficient. The compaction process will produce a new AoL which in turn will be used for subsequent writes.

### Partitioning

- ToyDb computes a hash based on value keys and uses the modulo of these keys to assign to one of the available partitions. This helps ensure that values are uniformly distributed across partitions to prevent partition hot spots. However, it also means that user values are not adjacent, which could prove problematic if we need to support transactions and range queries in the future.
