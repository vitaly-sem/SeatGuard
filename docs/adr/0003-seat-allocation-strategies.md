# ADR-0003: Pessimistic by default, optimistic as a selectable alternative

- Status: accepted
- Date: 2026-10-23

## Context

Many customers can attempt to hold the same seat concurrently. Two common
approaches exist:

1. Lock the row, let competitors wait and then observe the new state.
2. Read without a lock, update conditionally, and retry or fail on conflict.

Both are correct with a database guard; they fail differently under load.

## Decision

Implement both behind `ISeatAllocationStrategy` and select one with
`Reservations:ConcurrencyStrategy`.

- `PessimisticSeatAllocationStrategy` runs `SELECT … FOR UPDATE` for the seat,
  validates availability and assigns the hold inside the same transaction.
- `OptimisticSeatAllocationStrategy` reads the seat, updates it with a
  `WHERE version = @expected` style check backed by `xmin`, and reports a
  concurrency conflict when the row changed.
- The default is **Pessimistic**: predictable latency and no retry storm when a
  popular seat is contested.

`SKIP LOCKED` is not used here. It would let a competitor skip the locked row
and incorrectly conclude the seat is unavailable, converting a valid queue into
a spurious conflict.

## Consequences

- The same application handler works with either strategy; tests are a theory
  over both.
- Pessimistic holds a row lock for the duration of a short transaction. Long
  work (payment) is deliberately outside it.
- Optimistic can waste work under high contention; it is best for low-collision
  workloads and read-heavy replicas.
- The partial unique index (ADR-0004) makes both strategies safe even if their
  logic regresses.

## Alternatives considered

- Advisory locks keyed by seat id: workable but harder to observe and reason
  about than row locks.
- A serializable isolation level: correct but expensive and prone to retries for
  unrelated rows.
