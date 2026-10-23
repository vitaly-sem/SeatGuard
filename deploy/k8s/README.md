# Kubernetes deployment

Kustomize overlays for a `seatguard` namespace (production) and `seatguard-dev`
(local cluster with a single PostgreSQL StatefulSet).

## Prerequisites

- Kubernetes 1.29+.
- An ingress controller in the `ingress-nginx` namespace (or patch the
  NetworkPolicy namespace selector).
- An OTLP collector reachable at `otel-collector.observability.svc.cluster.local:4317`
  for traces; the Prometheus endpoint works without it.
- `kube-prometheus-stack` in the `monitoring` namespace for the ServiceMonitor
  and PrometheusRule (included only by the production overlay).

## Development overlay

```bash
docker build -t seatguard-api:latest -f src/SeatGuard.Api/Dockerfile .
kubectl apply -k deploy/k8s/overlays/dev
kubectl -n seatguard-dev rollout status deployment/seatguard-api
kubectl -n seatguard-dev port-forward svc/seatguard-api 8080:80
```

The console is then at <http://localhost:8080/ui/> with the development admin
key from the generated Secret (`dev-admin-key`).

## Production overlay

```bash
cp deploy/k8s/overlays/prod/secrets.env.example deploy/k8s/overlays/prod/secrets.env
# edit secrets.env: connection string and a strong Admin__ApiKey
# edit the image tag and ingress host in kustomization.yaml / ingress.yaml
kubectl apply -k deploy/k8s/overlays/prod
kubectl -n seatguard rollout status deployment/seatguard-api
```

The migration Job uses the same image with `--migrate`. Kubernetes applies the
Job and the Deployment together; the API becomes Ready once the schema exists.
Before an upgrade, delete the completed Job so it can run again:

```bash
kubectl -n seatguard delete job seatguard-migrate --ignore-not-found
kubectl apply -k deploy/k8s/overlays/prod
```

`secrets.env` is gitignored. In a real cluster prefer External Secrets or your
provider's secret manager instead of a local file.

## What is included

| Resource | Purpose |
| --- | --- |
| Deployment | 2 replicas in base, 3 in production, rolling updates, probes, read-only root filesystem |
| Service + Ingress | ClusterIP on port 80, nginx Ingress in production |
| HPA | CPU 70% / memory 80%, 3–12 replicas, scale-down stabilisation |
| PDB | Keeps at least one API pod during voluntary disruptions |
| Migration Job | `dotnet SeatGuard.Api.dll --migrate`, retries, TTL cleanup |
| NetworkPolicy | Ingress from ingress-nginx/monitoring, egress to DNS, PostgreSQL and OTLP |
| ServiceMonitor + PrometheusRule | `/metrics` scraping and alerts for availability, 5xx, latency and conflicts |
