# ADR-0008: RFC 9457 Problem Details with stable codes

- Status: accepted
- Date: 2026-10-23

## Context

Clients need to distinguish "the seat is gone" from "you sent a bad request" and
from "the server is broken" without parsing prose. Status codes alone are not
expressive enough.

## Decision

Return `application/problem+json` (RFC 9457) for every error, with:

- `status`, `title`, `detail`, `instance`, `type`.
- `code`: a stable machine-readable identifier such as `seat.not_available`,
  `seat.conflict`, `reservation.expired`, `reservation.invalid_state`,
  `request.invalid`, `resource.not_found`, `event.not_on_sale`,
  `payment.declined`, `idempotency.key_in_progress` and `server.error`.
- `traceId` and the `X-Correlation-ID` response header.
- `errors`: a camelCase field-to-messages map for validation failures.

Domain exceptions are mapped centrally; the API never exposes stack traces or
database messages. Validation runs in a pipeline behaviour before the handler,
so a 400 does not open a transaction.

## Consequences

- Clients can branch on `code` and show localized messages.
- Codes are a public contract; renaming one is a breaking change.
- The console maps common codes to concise toasts.
- 404 responses for unknown resources are intentionally indistinguishable from
  unauthorized existence (when auth is added).

## Alternatives considered

- A custom envelope with `{ success, error }`: less standard and loses HTTP
  semantics.
- Raw exception messages: leak internals and vary between releases.
