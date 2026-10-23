# ADR-0005: Customer-scoped idempotency keys for hold creation

- Status: accepted
- Date: 2026-10-23

## Context

Clients retry on timeouts. Without idempotency, a retried hold either creates a
duplicate reservation or fails with a confusing conflict even though the first
request succeeded.

## Decision

Accept an optional `Idempotency-Key` header on `POST /api/v1/reservations`.

- Keys are scoped to `(customerId, idempotencyKey)`, not global.
- On replay, return the existing reservation with `200 OK` and the original id.
- The first request returns `201 Created` with a `Location` header.
- A partial unique index enforces the pair at the database level; a race between
  two identical requests is translated to
  `409 idempotency.key_in_progress` so the client can retry.
- Keys are trimmed and blank keys are treated as absent.

Replays return the original reservation even if its status has since moved to
Confirmed or Expired: the operation is "get or create", not "repeat the state
transition".

## Consequences

- Safe retries for the most common failure: a lost response.
- Clients must keep the key stable across retries; generating a new UUID per
  attempt defeats the mechanism (the console demonstrates both modes).
- Keys live forever in this design. A retention job would be needed for very
  high volumes.

## Alternatives considered

- A global key: simpler, but lets one customer's key collide with another's and
  leaks existence across tenants.
- Server-generated request ids: do not help a client that never received the
  first response.
