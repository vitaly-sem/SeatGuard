# ADR-0009: Serilog, OpenTelemetry and Prometheus

- Status: accepted
- Date: 2026-10-23

## Context

A concurrency bug is often visible only in production under load. Logs without
correlation, metrics without traces, and traces without resource attributes are
each insufficient on their own.

## Decision

- **Serilog** writes compact JSON to stdout, enriched with service name,
  environment, machine, `CorrelationId` and `TraceId`. Request logging is
  enabled; health, metrics and static UI requests log at Debug.
- **OpenTelemetry** instruments ASP.NET Core, `HttpClient`, EF Core and Npgsql,
  with a resource describing the service and environment. Traces and metrics
  export over OTLP only when `Observability:OtlpEndpoint` is set, so local runs
  stay quiet.
- **Prometheus** scrapes `/metrics`; custom instruments use the
  `SeatGuard.Reservations` meter and stable names such as
  `seatguard.reservations.created`.
- **Health** distinguishes liveness (`/health/live`, no dependencies) from
  readiness (`/health/ready`, includes a PostgreSQL check and returns a JSON
  report).

Correlation ids are accepted from `X-Correlation-ID` (bounded to 128 chars),
otherwise derived from the trace id and echoed back.

## Consequences

- A slow request can be followed from log line to span to database statement.
- The observability stack is optional: no exporter, no network dependency.
- High-cardinality values (reservation ids, seat ids) are not metric labels;
  they belong in traces and logs.
- Serilog is disabled under the `Testing` environment to keep test output
  readable; integration tests still exercise the API without telemetry overhead.

## Alternatives considered

- Plain `ILogger` text logs: workable, but harder to query in a log store.
- Only metrics: cannot explain a single failed reservation.
- Vendor SDKs directly: lock-in and heavier configuration.
