# ADR-0004: Database constraints as the final correctness barrier

- Status: accepted
- Date: 2026-10-23

## Context

Application-level checks are required for good error messages, but they are not
a proof. A missed code path, a future endpoint or a race between replicas must
not be able to sell one seat twice.

## Decision

Encode the invariants in PostgreSQL as well:

- Partial unique index `ux_reservations_active_seat` on `reservations(seat_id)`
  where `status IN ('Pending', 'Confirmed')`.
- Unique index on `(customer_id, idempotency_key)` for non-null keys.
- Unique index on `ticket_code` for non-null codes.
- Check constraints for positive prices, valid seat numbers and allowed status
  values.
- A seat can only be held by the reservation recorded in
  `seats.held_by_reservation_id`.

`UnitOfWork` translates unique-violation constraint names into domain
exceptions (`SeatUnavailableException`, `DuplicateIdempotencyKeyException`)
instead of leaking `DbUpdateException` to the API.

## Consequences

- The worst case under a logic bug is a 409, not a double booking.
- Error translation is tied to constraint names; renaming a constraint requires
  updating the mapping.
- The partial index adds write cost on reservation insert, negligible relative
  to correctness.

## Alternatives considered

- Relying only on `SELECT … FOR UPDATE`: correct for the current path, but does
  not protect against a new writer that forgets to lock.
- Deferred constraints with triggers: more complex and harder to test.
