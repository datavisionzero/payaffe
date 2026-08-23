# Observability Baseline

This document records the baseline for technical observability.

## Target State

`payaffe` provides observability through external self-hosted services:

- Grafana LGTM Stack for logs, dashboards, traces, and metrics.
- GlitchTip for error reporting and exception monitoring.

The Grafana LGTM Stack target includes Loki for logs, Grafana for dashboards, Tempo for traces, and Mimir for metrics.

An operator running several applications may point all of them at one
observability stack rather than running one per application. A dedicated stack
remains fine for local development, tests, or a documented operational
deviation.

## Telemetry Standard

Technical telemetry is instrumented with OpenTelemetry.
Logs, traces, and metrics should be exported through OTLP to Grafana Alloy as
the central collector or gateway. From there, signals are routed into the
Grafana LGTM Stack. The OpenTelemetry Collector remains allowed as a documented
deviation when a product or operational context specifically needs it.

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

Error tracking is provided separately through GlitchTip with Sentry-compatible SDKs. GlitchTip is not the general OTLP receiver for technical logs, traces, and metrics.

For .NET, `Microsoft.Extensions.Logging`, `Activity`, and `Meter` remain the preferred local APIs. Export is handled through the OpenTelemetry .NET SDK.

Serilog may be used in .NET hosts as the structured logging implementation and is useful for production hosts when it improves bootstrap logging, structured JSON output, enrichment, and request logging. Application code still depends only on `ILogger<T>` from `Microsoft.Extensions.Logging`. Serilog sinks to specific vendor systems are not the standard path; OpenTelemetry/OTLP or structured output collected by Grafana Alloy remain preferred.

For Next.js and Node.js, server-side logs are structured and connected through OpenTelemetry where stable. Browser-side error reporting primarily uses the GlitchTip or Sentry-compatible SDK; broader browser telemetry needs a separate privacy and sampling decision.

## Local Log Output For Operators

Operators without their own Grafana LGTM Stack still need usable technical logs. Applications therefore emit structured logs at least to `stdout`/`stderr`, preferably as JSON and suitable for Docker, systemd, or simple log collectors.

Optional rolling file logs are allowed for self-hosted installations when they are configurable and include rotation, retention, size limits, and safe file permissions. File logs are an operator fallback, not a replacement for central observability.

Normal end users do not receive raw technical logs in the product UI. They receive appropriate status, error, or support information. Technical logs remain an operator, support, and development tool.

## Production Operations Baseline

This operations baseline is a recommendation for production setups. Product
repositories should adopt it; deviations are allowed but must be documented in
the product or operations context.

Production hosts should provide these minimum signals:

- structured logs on `stdout`/`stderr`,
- OTLP export for logs, traces, and metrics,
- error reports to GlitchTip or a Sentry-compatible target.

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
