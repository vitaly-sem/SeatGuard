# ADR-0002: PostgreSQL as the system of record, EF Core with snake_case

- Status: accepted
- Date: 2026-10-23

## Context

Seat allocation needs strong consistency, row-level locking, partial unique
indexes and reliable transactions. The team also wants a schema that is
readable to DBAs without an ORM dictionary.

## Decision

Use PostgreSQL 17 and EF Core 10 with the Npgsql provider and
`UseSnakeCaseNamingConvention`.

- Money is stored as `numeric(10,2)` with an ISO currency code.
- Timestamps are `timestamptz`.
- Seat sections are a JSONB owned collection on the event.
- `seat.version` maps to PostgreSQL's `xmin` system column for optimistic
  concurrency; it is not a separate application-managed column.
- Migrations are generated with the local `dotnet-ef` tool pinned in
  `dotnet-tools.json`.

## Consequences

- One database provides transactions, locking and the outbox without an extra
  broker for correctness.
- Snake_case keeps raw SQL in the sweeper and dispatcher readable.
- `xmin` is PostgreSQL-specific; the optimistic strategy is not portable as-is.
- JSONB sections trade relational normalization for a compact event aggregate.

## Alternatives considered

- SQL Server: capable, but the deployment target and local tooling favor
  PostgreSQL.
- Dapper with hand-written SQL: more control, more mapping code and no change
  tracking for the outbox.
- A distributed database: unnecessary at this scale and would complicate
  `FOR UPDATE` semantics.
