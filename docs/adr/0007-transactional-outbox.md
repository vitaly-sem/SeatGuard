# ADR-0007: Transactional outbox with at-least-once delivery

- Status: accepted
- Date: 2026-10-23

## Context

Publishing an event after committing a reservation creates a dual-write problem:
the commit can succeed and the publish fail, or the process can die between
them. Publishing before the commit can announce a reservation that rolls back.

## Decision

Write outbox rows in the same transaction as the aggregate change.

- `SeatGuardDbContext.SaveChangesAsync` converts collected domain events into
  `outbox_messages` rows.
- `OutboxDispatcher` claims due rows in a short transaction with
  `FOR UPDATE SKIP LOCKED` and a lease (`next_attempt_at = now + lease`), commits,
  then publishes outside any transaction.
- Success marks `processed_at`; failure increments `attempts`, stores the error,
  schedules exponential backoff (2, 4, 8 … capped at 300 seconds) and sets
  `failed_at` at `Outbox:MaxAttempts` (10).
- The lease makes rows invisible to other replicas during publish. If a process
  dies mid-publish, the lease expires and the row is redelivered.

Delivery is **at-least-once**. A crash after publish but before the mark causes a
duplicate; consumers must be idempotent (keyed by message id or aggregate id).

## Consequences

- No distributed transaction and no lost events on process failure.
- Duplicates are possible by design; this is documented in the publisher
  contract.
- The database is both the queue and the source of truth; outbox growth needs
  retention/archival in production.
- The default publisher logs structured events; swapping in Kafka/SNS/SQS does
  not change the dispatcher.

## Alternatives considered

- `LISTEN/NOTIFY`: no durability and loses events during downtime.
- Debezium/CDC: elegant, but adds operational infrastructure for a single
  service.
- Exactly-once: not achievable end-to-end without consumer cooperation; the
  honest contract is at-least-once.
