# ADR-0010: Integration tests against real PostgreSQL

- Status: accepted
- Date: 2026-10-23

## Context

The core claim of this project is concurrency safety. Mocking repositories or
using the EF Core in-memory provider would test the mocks, not PostgreSQL locks,
partial indexes or `xmin`. Unit tests cannot prove that 40 simultaneous holds
produce one winner.

## Decision

Use Testcontainers with `postgres:17-alpine` and `WebApplicationFactory<Program>`.

- One container per test run; a fresh host per test.
- Respawn truncates all tables except `__EFMigrationsHistory` before each host.
- A shared `FakeTimeProvider` is injected into DI; only expiry tests advance it.
- Early-read configuration (connection string, admin key, feature toggles) is set
  through environment variables before the host starts, because `Program` reads
  them while registering services, before `WebHostBuilder` callbacks apply.
- Both allocation strategies run as a `[Theory]` with 40 concurrent requests.
- Background services are disabled and invoked explicitly through
  `IExpiredReservationSweeper` / `IOutboxDispatcher`.

Domain and application rules remain covered by fast unit tests (67).

## Consequences

- Tests catch real SQL, migration and constraint regressions.
- Docker is required for the full suite; CI provides it.
- Tests are slower than pure unit tests (about 2 seconds for 15 integration
  tests after the image is cached).
- A broken migration fails every test, which is desirable.

## Alternatives considered

- EF Core in-memory: does not support transactions, raw SQL, row locks or
  partial indexes.
- SQLite: different locking and SQL semantics; would give false confidence.
- A shared long-lived database: faster, but state leaks between tests and makes
  failures order-dependent.
