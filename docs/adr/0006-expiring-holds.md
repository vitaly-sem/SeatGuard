# ADR-0006: Expire holds with a periodic SKIP LOCKED sweeper

- Status: accepted
- Date: 2026-10-23

## Context

A hold is a promise with a deadline. If the customer never pays, the seat must
return to inventory. Doing this lazily on read makes availability unpredictable;
a database timer per hold does not scale; a single-worker loop is fragile across
replicas.

## Decision

Run `ExpiredReservationSweeper` as a hosted service on a fixed interval
(15 seconds by default, configurable, batch size 100).

- Claim a batch with
  `SELECT … WHERE status = 'Pending' AND expires_at <= now ORDER BY expires_at LIMIT n FOR UPDATE SKIP LOCKED`.
- Re-check the domain transition (`Expire`) while the rows are locked.
- Release the seat only if `held_by_reservation_id` still points at this
  reservation; otherwise log and leave it alone.
- Write `ReservationExpired` to the outbox in the same transaction and invalidate
  the availability cache.
- Each batch is its own transaction; a failure is retried on the next tick.
- Expose `SweepOnceAsync` so tests can drive it with a fake clock.

`SKIP LOCKED` is appropriate here because work is homogeneous: any replica may
process any expired row, and skipping a row another replica owns is the desired
distribution, not an error.

## Consequences

- Horizontal scaling is safe without leader election.
- A hold may remain visible up to one interval after its TTL. Confirm already
  rejects an expired reservation with 410, so correctness does not depend on the
  sweeper running promptly.
- A poison batch rolls back and retries; persistent failures need an alert.
- The sweeper is a point of coupling to the schema; the raw SQL is covered by an
  integration test.

## Alternatives considered

- Expire inline on availability reads: couples read latency to writes, and reads
  from cache would miss it.
- `pg_cron`: adds an extension dependency and moves logic out of the application.
- Per-reservation delayed messages: requires a broker and loses the simple
  transactional story.
