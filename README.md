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
- Replication
- Partitioning
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
