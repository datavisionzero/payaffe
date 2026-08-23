# Observability Baseline

This document records the baseline for technical observability.

## Target State

`payaffe` provides observability through external self-hosted services:

- logaffe for logs, errors among them.
- Grafana LGTM Stack for dashboards, traces, and metrics.

Two targets, because neither takes what the other does: logaffe accepts log
entries and neither spans nor time series. An error is an entry and is not sent
anywhere separately ([ADR 0026](../adr/0026-an-error-is-an-entry-and-there-is-no-error-tracker.md)).
The Grafana LGTM Stack target here is Grafana for dashboards, Tempo for traces,
and Mimir for metrics. Loki is not part of it, because logaffe is where logs
go.

An operator running several applications may point all of them at one
observability stack rather than running one per application. A dedicated stack
remains fine for local development, tests, or a documented operational
deviation.

## Telemetry Standard

Technical telemetry is instrumented with OpenTelemetry.
Traces and metrics should be exported through OTLP to Grafana Alloy as the
central collector or gateway. From there, signals are routed into the Grafana
LGTM Stack. The OpenTelemetry Collector remains allowed as a documented
deviation when a product or operational context specifically needs it.

Logs do not travel this path. They are delivered to a logaffe installation
through its `ILogger` provider ([ADR 0025](../adr/0025-logs-are-delivered-to-logaffe.md)).

Structured logs must be correlatable with traces and metrics. At minimum, these technical fields are expected:

- `service.name`
- `service.version`
- `deployment.environment`
- `trace_id`
- `span_id`
- `correlation_id`
- `severity`
- `message`

HTTP, database, worker, blockchain observation, webhook, and external provider contexts should use structured attributes, such as `http.method`, `http.route`, `http.status_code`, `db.system`, `db.operation`, or domain-neutral provider and job attributes.

There is no separate error-tracking service. An error is a log entry at `Error` or above and reaches logaffe with everything else; grouping, issue state, and release regression are given up deliberately and the cost is recorded in ADR 0026.

For .NET, `Microsoft.Extensions.Logging`, `Activity`, and `Meter` remain the preferred local APIs. Trace and metric export is handled through the OpenTelemetry .NET SDK.

Log delivery is handled by `Logaffe.Extensions.Logging`, an `ILoggerProvider` that is additive: the console provider stays, and application code still depends only on `ILogger<T>`. It reads `Activity.Current`, so an entry carries the trace and span of the request it belongs to and correlates with what OTLP exported without the application passing anything.

Serilog may be used in .NET hosts as the structured logging implementation when it improves bootstrap logging, structured JSON output, enrichment, or request logging. `Logaffe.Serilog` is the delivery path in that case. Application code still depends only on `ILogger<T>`.

For Next.js and Node.js, server-side logs are structured and connected through OpenTelemetry where stable. Browser-side errors are posted to the product's own API and logged there rather than to a third-party SDK, which keeps the browser bundle free of any installation-specific value; broader browser telemetry needs a separate privacy and sampling decision.

## Local Log Output For Operators

Operators without a logaffe installation still need usable technical logs. Applications therefore emit structured logs at least to `stdout`/`stderr`, preferably as JSON and suitable for Docker, systemd, or simple log collectors.

Optional rolling file logs are allowed for self-hosted installations when they are configurable and include rotation, retention, size limits, and safe file permissions. File logs are an operator fallback, not a replacement for central observability.

Normal end users do not receive raw technical logs in the product UI. They receive appropriate status, error, or support information. Technical logs remain an operator, support, and development tool.

## Production Operations Baseline

This operations baseline is a recommendation for production setups. Product
repositories should adopt it; deviations are allowed but must be documented in
the product or operations context.

Production hosts should provide these minimum signals:

- structured logs on `stdout`/`stderr`,
- log delivery to a logaffe installation, errors included,
- OTLP export for traces and metrics.

Host-local agents are not mandatory by default. They may be used when
operations, platform, network boundaries, or scaling require them.

Direct vendor sinks from application code are not the standard path.
Applications provide technical telemetry through local APIs, structured output,
and OTLP; the observability stack owns ingestion, routing, storage, and
analysis.

## Retention And Sampling

Recommended minimum retention for technical observability data:

- Logs: 14 days
- Traces: 7 days
- Metrics: 30 days
- Error reports: 90 days

These values apply to technical telemetry and error reports, not
security-relevant audit events. Audit retention is governed by
[admin-security-baseline.md](admin-security-baseline.md).

Trace sampling is allowed in production. Sampling rules should be documented in
the product or operations context. Error traces and security- or
operations-relevant paths should be preferentially retained.

## Dashboards And Alerts

Production setups should provide minimum dashboards for these views:

- host and service status,
- HTTP/API,
- workers or jobs, when present,
- database,
- error reports.

Payment-specific dashboards may add blockchain observation, webhook delivery,
payment-state, and provider availability views.

Production setups should provide minimum alerts for these states:

- host down,
- readiness unavailable for a sustained period,
- database unreachable,
- elevated error rate,
- no telemetry from production hosts.

## Boundaries

Observability services are operational services, not product domain logic embedded in each application. Applications emit structured logs, technical metrics, traces, and error events to those services.

Health checks remain useful as simple operations and deployment signals, but they do not replace the shared observability baseline.

Product audit logs, Payment Event History, Webhook Delivery History, and Reorg Alerts remain separate from technical logs, traces, metrics, and error reports. Those product records explain payment and operator activity; observability supports operations, debugging, performance analysis, and technical alerting.

## Privacy and Security

Observability data must not contain passwords, tokens, secrets, wallet material, bearer tokens, webhook secrets, or full domain payload dumps. Personal data, payment context fields, provider payloads, and blockchain observation details should be minimized.

Correlation between logs, traces, metrics, and error reports should use technical identifiers such as correlation IDs, request IDs, and trace IDs, not human-readable personal data.

## Implementation

Not every repository needs technical instrumentation immediately. This
baseline records the shared target and recommended production operations
baseline. Concrete SDK packages, exporter configuration, sampling rates,
dashboard definitions, alert thresholds, and deployment topologies are decided
per product or installation.
