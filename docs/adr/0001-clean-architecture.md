# ADR-0001: Split the system into Domain, Application, Infrastructure and Api

- Status: accepted
- Date: 2026-10-23

## Context

The booking rules (a seat can be held once, holds expire, confirmed seats are
final) are the part most likely to change and the part most expensive to get
wrong. Persistence, HTTP and scheduling concerns should not leak into them.

## Decision

Use four projects with dependencies pointing inward:

- `SeatGuard.Domain` has no framework references.
- `SeatGuard.Application` depends on Domain and declares ports (repositories,
  payment gateway, cache, allocation strategy, sweeper, dispatcher).
- `SeatGuard.Infrastructure` implements those ports with EF Core, PostgreSQL,
  in-memory caching and background services.
- `SeatGuard.Api` composes the graph, exposes endpoints and owns HTTP concerns.

Use cases are MediatR commands and queries with a pipeline of logging,
validation and transaction behaviours, in that order. Domain events are raised
by aggregates and persisted through the outbox by the DbContext.

## Consequences

- Domain and application rules are testable without a database or HTTP host.
- Adding a transport (gRPC, worker) does not require touching the domain.
- The mapping layer between aggregates and DTOs is explicit and small.
- More projects and files than a single-project solution; intentional for the
  boundaries it makes visible.

## Alternatives considered

- Vertical slices in one project: faster to start, but hides the dependency
  direction and makes it easy to put SQL inside handlers.
- Full CQRS with separate read/write stores: rejected as premature; the
  availability read model is a cached projection of the same tables.
