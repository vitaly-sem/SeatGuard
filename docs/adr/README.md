# Architecture decision records

| ADR | Decision |
| --- | --- |
| [0001](0001-clean-architecture.md) | Split the system into Domain, Application, Infrastructure and Api |
| [0002](0002-postgresql-and-ef-core.md) | PostgreSQL as the system of record, EF Core with snake_case |
| [0003](0003-seat-allocation-strategies.md) | Pessimistic by default, optimistic as a selectable alternative |
| [0004](0004-database-invariants.md) | Database constraints as the final correctness barrier |
| [0005](0005-idempotent-reservations.md) | Customer-scoped idempotency keys for hold creation |
| [0006](0006-expiring-holds.md) | Expire holds with a periodic SKIP LOCKED sweeper |
| [0007](0007-transactional-outbox.md) | Transactional outbox with at-least-once delivery |
| [0008](0008-problem-details.md) | RFC 9457 Problem Details with stable codes |
| [0009](0009-observability.md) | Serilog, OpenTelemetry and Prometheus |
| [0010](0010-testcontainers-testing.md) | Integration tests against real PostgreSQL |
| [0011](0011-deployment.md) | One image, a migration mode and Kustomize overlays |
| [0012](0012-api-served-console.md) | Serve the booking console from the API |
