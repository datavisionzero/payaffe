# Observability

`payaffe` follows the shared observability baseline in
[../architecture/observability-baseline.md](../architecture/observability-baseline.md):
structured logs on stdout delivered to a logaffe installation, OpenTelemetry
for traces and metrics with OTLP export to a collector, and GlitchTip for error
reports.

## What Every Host Emits

The API, worker, and Admin MCP hosts share one telemetry setup.

- Structured JSON logs with scopes on stdout. The Admin MCP host writes its log
  lines to stderr instead, because stdout carries the MCP protocol.
- Those same entries delivered to a logaffe installation when one is configured
  ([ADR 0025](../adr/0025-logs-are-delivered-to-logaffe.md)). Delivery is
  additive and never replaces the console output.
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
| `PAYAFFE_CLIENT_ERRORS_ENABLED` | `Diagnostics:ClientErrors:Enabled` | Whether `POST /api/client-errors` is mapped. Default `true`. |
| `PAYAFFE_CLIENT_ERROR_RATE_LIMIT_PERMIT_LIMIT` | `Diagnostics:ClientErrors:RateLimitPermitLimit` | Reports accepted per source address per window. Default `10`. |
| `PAYAFFE_CLIENT_ERROR_RATE_LIMIT_WINDOW` | `Diagnostics:ClientErrors:RateLimitWindow` | The window. Default one minute. |
| `PAYAFFE_LOGAFFE_URL` | `Observability:Logaffe:Url` | Scheme and host of the logaffe installation, for example `https://logs.example.com`. The ingest path is appended by the client and is not a setting. Empty keeps logs on stdout only. |
| `PAYAFFE_LOGAFFE_TOKEN` | `Observability:Logaffe:IngestToken` | Ingest token. A secret, and also what names the logaffe project entries land in. |
| `PAYAFFE_OTLP_ENDPOINT` | `Observability:OtlpEndpoint` | OTLP receiver for traces and metrics, normally Grafana Alloy, for example `http://alloy:4317`. Logs do not travel this path. Empty disables export. |
| `PAYAFFE_DEPLOYMENT_ENVIRONMENT` | `Observability:DeploymentEnvironment` | `deployment.environment` resource attribute. |
| `PAYAFFE_RELEASE` | `Observability:ServiceVersion` | `service.version` resource attribute. |
| `PAYAFFE_BACKEND_GLITCHTIP_DSN` | `Observability:GlitchTipDsn` | Sentry-compatible DSN for backend error reports. Empty disables error reporting. |
| `PAYAFFE_OPERATIONAL_METRICS_SNAPSHOT_INTERVAL` | `Observability:OperationalMetrics:SnapshotInterval` | Between 5 seconds and 15 minutes. |

`OTEL_EXPORTER_OTLP_ENDPOINT` is accepted as an alternative to
`PAYAFFE_OTLP_ENDPOINT` for operators who already set the conventional variable.

### Browser errors

Errors the payer page and the Admin UI could not handle are posted back to this
installation at `POST /api/client-errors` and logged where everything else is
logged. That is why no separate browser error-tracking service is required for
them.

It is the only unauthenticated endpoint anybody on the internet may post to, and
it is bounded accordingly:

| Bound | Value |
| --- | --- |
| Request body | 32 KiB, enforced by the endpoint rather than by the server |
| Rate limit | `PAYAFFE_CLIENT_ERROR_RATE_LIMIT_PERMIT_LIMIT` per source address per window |
| Fields accepted | `name`, `message`, `stack`, `path` — nothing else is read |
| Level | `Warning` |

Three of those are security properties rather than tuning.

**Warning, not Error.** An unauthenticated caller must not be able to raise the
installation's error rate, because an error rate is what an alert is derived
from. Browser errors are found by filtering on `Warning` and the
`Payaffe.Web.ClientError` source, not by watching the error count.

**The release, the environment, the user agent and the address are not accepted
from the caller.** The host already knows all four, and taking them from the
body would only let the body choose them.

**The reported text is never part of a log message template.** It is passed as
values, so a browser reporting `{ClientErrorName}` gets those characters logged
rather than a substitution. Messages are reduced to one line, control characters
are removed, a query string is dropped from the path, and everything is cut to a
cap and flagged with an ellipsis. The stack trace is carried in the field a log
store keeps an exception in, so it is searchable there.

`PAYAFFE_CLIENT_ERRORS_ENABLED=false` leaves the endpoint unmapped. An
installation that does not want a publicly postable surface does not get one
that answers and discards.

### Log delivery

`PAYAFFE_LOGAFFE_URL` and `PAYAFFE_LOGAFFE_TOKEN` go together. Setting one
without the other fails startup with a message naming the missing key, rather
than starting a host whose logs go nowhere — a wrong OTLP endpoint shows up as
an empty dashboard, but missing logs are discovered at the moment somebody needs
them.

Delivery is fire-and-forget: a bounded in-memory queue that drops its oldest
entries when full, no durable buffer, and no retry that outlives the process. It
never blocks or throws into the host. A failed delivery is reported on stderr,
and the console log is still complete, which is what makes the loss affordable.

The logger category arrives as `SourceContext`, and the trace and span come from
the current `Activity`, so an entry in logaffe correlates with the span the same
request produced in Tempo. The instance identifier is the same value as the
`service.instance.id` resource attribute.

Issue one ingest token per installation and treat it like the GlitchTip DSN
below. Rotating it is a logaffe-side operation followed by a restart of the API,
worker, and MCP hosts.

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
