# Observability

`payaffe` follows the shared observability baseline in
[../architecture/observability-baseline.md](../architecture/observability-baseline.md):
structured logs on stdout, OpenTelemetry for traces and metrics, OTLP export to
a collector, and GlitchTip for error reports.

## What Every Host Emits

The API, worker, and Admin MCP hosts share one telemetry setup.

- Structured JSON logs with scopes on stdout. The Admin MCP host writes its log
  lines to stderr instead, because stdout carries the MCP protocol.
- Resource attributes `service.name` (`payaffe-api`, `payaffe-worker`,
  `payaffe-mcp`), `service.namespace` (`payaffe`), `service.version`,
  `service.instance.id`, and `deployment.environment`.
- Traces from ASP.NET Core (API only), `HttpClient`, and Npgsql.
- Metrics from the .NET runtime, `HttpClient`, Npgsql, and the product meter
  `Payaffe`.

Telemetry must not contain bearer tokens, Webhook secrets, provider API keys,
Extended Public Keys, TOTP material, Recovery Codes, or raw provider responses.
Payment Context Fields and Payment Addresses are product data and stay in the
product tables, not in metric attributes.

## Product Metrics

| Instrument | Type | Attributes | Meaning |
| --- | --- | --- | --- |
| `payaffe.worker.runs` | counter | `worker.name`, `outcome` | One background batch. `outcome` is `completed`, `skipped` when another instance holds the lease, or `failed`. |
| `payaffe.worker.consecutive_failures` | gauge | `worker.name` | Failed batches in a row for a named worker lease. |
| `payaffe.webhook.delivery.attempts` | counter | `result` | One Webhook Delivery attempt, including manual resend. `result` is `succeeded`, `retry_pending`, or `terminal_failed`. |
| `payaffe.webhook.terminal_failures` | gauge | – | Webhook Outbox events in a terminal failed state. |
| `payaffe.observation.unavailable` | gauge | `currency` | `1` while Blockchain Observation for that Supported Currency is unavailable. |
| `payaffe.address_pool.available` | gauge | `currency` | Unassigned native ETH Address Pool entries. |
| `payaffe.reorg_alerts.open` | gauge | – | Reorg Alerts still awaiting operator review. |

The gauges are read from the product tables by a periodic snapshot in every host
that runs the workers. `Observability:OperationalMetrics:SnapshotInterval`
controls the interval; `Observability:OperationalMetrics:Enabled` turns the
snapshot off.

The snapshot deliberately holds no worker lease. Every instance reports the same
database-wide values and the observability stack aggregates them, so the state
stays visible even when the lease holder is the unhealthy instance.

## Configuration

| Environment variable | Configuration key | Effect |
| --- | --- | --- |
| `PAYAFFE_OTLP_ENDPOINT` | `Observability:OtlpEndpoint` | OTLP receiver, normally Grafana Alloy, for example `http://alloy:4317`. Empty disables export and keeps structured logs. |
| `PAYAFFE_DEPLOYMENT_ENVIRONMENT` | `Observability:DeploymentEnvironment` | `deployment.environment` resource attribute. |
| `PAYAFFE_RELEASE` | `Observability:ServiceVersion` | `service.version` resource attribute. |
| `PAYAFFE_BACKEND_GLITCHTIP_DSN` | `Observability:GlitchTipDsn` | Sentry-compatible DSN for backend error reports. Empty disables error reporting. |
| `PAYAFFE_OPERATIONAL_METRICS_SNAPSHOT_INTERVAL` | `Observability:OperationalMetrics:SnapshotInterval` | Between 5 seconds and 15 minutes. |

`OTEL_EXPORTER_OTLP_ENDPOINT` is accepted as an alternative to
`PAYAFFE_OTLP_ENDPOINT` for operators who already set the conventional variable.

A GlitchTip DSN is a secret. Rotate it if it is exposed; see
[credential-rotation.md](credential-rotation.md).

## Dashboard And Alerts

[`deploy/observability/grafana-dashboard.json`](../../deploy/observability/grafana-dashboard.json)
is the versioned starting dashboard. Import it, or provision it through the
installation's Grafana configuration.

[`deploy/observability/prometheus-alerts.yaml`](../../deploy/observability/prometheus-alerts.yaml)
holds the minimum alert rules. Load them into the metrics ruler and configure an
installation-specific contact point before production traffic. Thresholds match
the shipped defaults; an installation with a different Address Pool size or
Webhook retry policy should adjust them and record the deviation.

## Worker Liveness

The worker container reports healthy while the host can still reach the
database, through a heartbeat file and the `dotnet Payaffe.Worker.dll health`
command that its Compose healthcheck runs. That is liveness only. It answers
"is this process alive and connected", not "is the scheduled work progressing";
the second question belongs to the worker alert rules and the lease table
below.

## Worker Diagnostics Without A Dashboard

Worker state is persisted, so it can also be read directly:

```sh
docker compose exec -T db psql --username "$PAYAFFE_DB_USER" --dbname "$PAYAFFE_DB_NAME" -c \
  "select worker_name, locked_by, locked_until, last_succeeded_at, last_failed_at, last_safe_error_code, consecutive_failure_count from app.background_worker_leases order by worker_name"
```

`locked_until` in the future means an instance currently holds the lease.
`consecutive_failure_count` resets to zero on the next successful batch.

## Verification After Deployment

1. Issue one successful and one rejected Integration API request; confirm both
   appear as correlated logs, traces, and metrics.
2. Confirm every expected `service.name` reports; the worker host is easy to
   forget when only the API is checked.
3. Wait one snapshot interval and confirm the gauges arrive.
4. Deliver one test Webhook Event and confirm
   `payaffe.webhook.delivery.attempts` increments with `result=succeeded`.
5. Trigger and acknowledge one test alert.

Record the installation-specific dashboard, alert, and GlitchTip URLs outside
this repository.
