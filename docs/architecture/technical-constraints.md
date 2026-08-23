# Technical Constraints

## Fixed

- Backend: .NET 10.
- Web frontend: React with Next.js App Router.
- Frontend package manager/runtime: pnpm via Corepack on Node.js 24.
- Browser- and UI-facing product frontends follow
  [frontend-baseline.md](frontend-baseline.md): generated API clients, TanStack
  Query, Tailwind CSS, shadcn/ui, React Hook Form, Zod, next-intl,
  accessibility rules, browser-storage rules, and the frontend test baseline.
- Primary database: PostgreSQL.
- Persistence: Entity Framework Core by default, with explicit SQL for performance-critical PostgreSQL features.
- PostgreSQL persistence and production migrations follow
  [database-conventions-baseline.md](database-conventions-baseline.md).
- Deployment: Docker Compose, as recorded in
  [deployment-operations-baseline.md](deployment-operations-baseline.md).
- Distribution model: open-source, self-hosted application.
- Instance model: single-tenant.
- MVP currencies: BTC, LTC, native ETH.
- Payment input fiat currencies: EUR and USD.
- Confirmation Requirements and Reorg Monitoring Depths are configurable per
  Supported Currency, with MVP defaults recorded in
  [../product/requirements.md](../product/requirements.md).
- Integration API contracts follow
  [integration-api-contract.md](integration-api-contract.md).
- MVP admin automation: MCP callable through an agent CLI.

## Operational

- The default deployment must not require storing complete blockchains.
- The system is optimized for low daily payment volume and low operating cost.
- The design may trade full-node-level validation for lower storage and operational burden.
- Blockchain Observation must isolate provider-specific API behavior from the common payment lifecycle so new supported providers can be added through software updates.
- The MVP is non-custodial: it must not store spending keys and must not perform refunds, sweeps, or withdrawals.
- Durable background work should start with .NET worker processes and PostgreSQL-backed jobs or outbox tables.
- Observability should use external self-hosted services according to [observability-baseline.md](observability-baseline.md): logaffe for logs, delivered through its `ILogger` provider; OpenTelemetry/OTLP as the telemetry standard for traces and metrics, with Grafana Alloy as the preferred collector and the Grafana LGTM Stack for dashboards, traces, and metrics; plus GlitchTip for error reporting. Production setups document telemetry retention, sampling, minimum dashboards, and minimum alerts as described there.
- Health and readiness endpoints, secret configuration, controlled migration
  runs, and PostgreSQL backups follow
  [deployment-operations-baseline.md](deployment-operations-baseline.md) and
  [secrets-configuration-baseline.md](secrets-configuration-baseline.md).

## Not Yet Decided

- Exact Hosted Blockchain API behavior for the selected Blockchain Observation Modes.
