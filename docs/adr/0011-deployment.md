# ADR-0011: One image, a migration mode and Kustomize overlays

- Status: accepted
- Date: 2026-10-23

## Context

The API and its schema evolve together. Deploying an image that starts serving
before migrations complete causes noisy readiness failures; running migrations
from every replica causes races. Local and production environments also differ
in database location and secrets.

## Decision

- Build one multi-stage image and publish it to a non-root
  `aspnet:10.0-noble-chiseled-extra` runtime.
- Add a `--migrate` mode to the same binary. It builds the service graph, applies
  migrations and exits without starting Kestrel.
- Kubernetes runs that mode as a `Job` (`seatguard-migrate`) before the
  Deployment's new pods become Ready. The API keeps
  `Database:MigrateOnStartup=false` in Kubernetes.
- Use Kustomize: a shared base plus `dev` (single replica, in-cluster Postgres,
  dev secret) and `prod` (3 replicas, HPA, Ingress, external database secret,
  ServiceMonitor and alerts).
- Manifests set requests/limits, probes, a read-only root filesystem, dropped
  capabilities, `runAsNonRoot`, topology spread and a PDB.
- CI renders both overlays and validates them with kubeconform, then builds and
  scans the image with Trivy before pushing to GHCR on `main`.

Local compose uses the same image with `Database:MigrateOnStartup=true`; it is a
single-container demo, not a production topology.

## Consequences

- The migration binary and the API are always the same version.
- The Job name is fixed; delete it before reapplying an upgraded Job.
- The production overlay expects an operator-managed Prometheus and an OTLP
  collector; both are documented as prerequisites, not bundled.
- The base image tag must be bumped (Dependabot helps) to pick up runtime
  security fixes.

## Alternatives considered

- EF migrations bundle: works, but adds a second artifact and a second version to
  track.
- Init container in the Deployment: guarantees ordering per pod but repeats the
  migration for every replica and makes rollout logs noisier.
- Helm: more powerful templating, but Kustomize keeps the rendered manifests
  reviewable and needs no chart release process for one service.
