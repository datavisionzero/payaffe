# Deployment And Operations Baseline

This document records the target deployment and operations baseline for
`payaffe`.

## Scope

The baseline covers:

- Docker Compose service boundaries and naming,
- external names and reverse-proxy assumptions,
- health and readiness endpoints,
- secret and runtime configuration use during deployment,
- controlled production migrations,
- PostgreSQL backup and restore expectations,
- product-specific operational data that must survive recovery,
- logs and observability signals needed for operation.

It does not create deployable hosts, Compose files, reverse-proxy
configuration, migration projects, dashboards, alerts, or CI jobs before the
related implementation exists.

## Deployment Model

The MVP deployment model is one Single-Operator Installation serving one
operator or shop, with one or more isolated Projects sharing the deployment.

Docker Compose is the minimum baseline for local development and self-hosted
operation. Other platforms may be added later, but they must not replace the
Compose baseline as the documented self-hosting path without a product
architecture decision.

Concrete deployment artifacts are added only when deployable hosts exist.
Placeholder services, unused target folders, sample jobs without a runnable
host, and speculative reverse-proxy files are not part of this baseline.

## Expected Services

When the corresponding hosts exist, Compose services use these names:

| Service | Purpose |
| --- | --- |
| `api` | ASP.NET Core host for the Integration API, browser APIs, health endpoints, and built Payer/Admin SPA assets. |
| `mcp` | Admin MCP host if it is deployed separately from another host. |
| `worker` | .NET background worker for Blockchain Observation, scheduler, outbox, and retry work. |
| `db` | PostgreSQL product database. |
| `migrations` | Manual schema run and the first-admin command. |

`apps/web` is a frontend source package, not a long-running service. The API
image builds it in a Node.js 24 plus pnpm stage and copies the static output
into the .NET runtime image. Production does not publish or operate a separate
web image or port.

Not every remaining service must exist as a separate container in the first
implementation. Combining hosts is allowed when the implementation keeps
surface boundaries, configuration, logs, health, and authorization clear.

An external identity provider is not part of the MVP local Admin Account
baseline. If external or central Admin, API, MCP, delegated, or CLI/device
flows are introduced later, the deployment must adopt them through a new ADR.

## External Names

The preferred external name is:

- `app.<domain>` for the Payer Page, Admin UI, Integration API, and health
  endpoints,
- `mcp.<domain>` only if a future remote MCP host is deliberately exposed.

An additional API-only name may reverse proxy the same API host when an
operator needs one, but the shipped browser application always uses its own
origin. The reverse proxy no longer splits browser and API traffic between two
product containers.

Remote MCP over HTTP is not part of the MVP baseline. Local agent CLI access
remains the first admin MCP path.

## Health And Readiness

HTTP hosts expose:

- `/health/live`,
- `/health/ready`.

`/health/live` reports that the process is running and reachable. It must not
perform deep dependency checks.

`/health/ready` reports whether the host can accept its intended traffic. It
must report whether the schema is current, and may check required
configuration, PostgreSQL connectivity, and required internal dependencies. It must not expose secrets, connection
strings, token values, provider keys, raw configuration values, payment data,
or provider payloads.

Readiness for payment-facing paths must account for the host's own ability to
serve requests. It does not require every Supported Currency or Hosted
Blockchain API provider to be healthy. Currency-specific Observation Health is
reported through product status views and Admin UI state.

Workers without an HTTP endpoint need a Compose healthcheck or equivalent
process-near health signal. Worker health must make it visible whether the
worker process is alive and whether durable work processing is materially
stuck.

## Configuration And Secrets

Runtime configuration follows
[secrets-configuration-baseline.md](secrets-configuration-baseline.md).

The self-hosting minimum is server-side environment variables and
non-versioned `.env` files with restrictive filesystem permissions. Container
images and build artifacts must not require production secrets.

Operations documentation must define concrete configuration keys once hosts
exist. `.env.example` may be versioned at that point and must contain only safe
placeholders or local-only non-production defaults.

Start-critical production configuration must fail fast or fail readiness when
missing or invalid. Optional integrations, such as Hosted Blockchain API keys,
Exchange Rate Source keys, OTLP export, or log delivery, may be disabled when
their configuration is missing, but the disabled state must be visible without
leaking secret values.

## Migrations

Production database migrations run through one path, and the `api` and `worker`
hosts take it as they start
([ADR 0027](../adr/0027-migrations-apply-on-startup.md)). The `mcp` host never
applies a migration; it is a local process that takes the schema as given. The
static application has no host process of its own.

The path must make failure visible, log enough context for diagnosis, and
handle concurrent execution safely — both hosts start together and either may
be first. It must not block the host from starting: a database that is not yet
reachable is waited for, while a migration that cannot be applied stops the
host with a non-zero exit code.

The `migrations` Compose service runs the same code for an operator who wants
the schema applied before anything else is started, and carries the first-admin
command ([ADR 0020](../adr/0020-the-first-admin-is-created-by-a-local-command.md)).

Local development and automated tests may use more convenient database
initialization, provided that behavior is not documented as the production
rule.

## Backups

The PostgreSQL product database is the primary backup target.

A PostgreSQL backup must preserve enough data to recover:

- Projects, their status, Project configuration, and ownership links,
- Payments and Payment Event History,
- Matching Blockchain Transactions and payment evidence,
- Rate Locks and Rate Cache state needed for explanation,
- Webhook Endpoints, outgoing Webhook Event outbox records, Webhook Delivery
  attempts, retry state, and terminal delivery state,
- Integration API Credential records and token hashes,
- Admin Accounts, session-relevant persisted state where applicable, MFA
  metadata, and recovery-code hashes,
- Audit Log entries and Audit export records,
- Address Pool entries, imports, assignment state, and capacity state,
- durable jobs, scheduler state, leases that are safe to recover, and
  dead-letter records,
- product configuration stored in PostgreSQL.

Address Pool data is operationally important even though public receiving
addresses are not spending secrets. A restore must not make already assigned
native ETH addresses appear unused or move any address to another Project.

The operator's external wallet seeds, private keys, watch-only wallet source
exports, Hosted Blockchain API accounts, DNS, TLS material, reverse-proxy
configuration, and off-repository secret store are outside the product
database backup. Operations documentation must tell operators to back them up
separately where they are required.

Backups must not include private keys, seed phrases, keystores, wallet
recovery phrases, plaintext Integration API bearer tokens, plaintext Admin
passwords, plaintext recovery codes, or other raw secret values that `payaffe`
must not store.

## Restore Expectations

A restore procedure must exist before production use.

At minimum, the procedure must cover:

- restoring the PostgreSQL database into a compatible application version,
- applying or verifying migrations through the controlled migration path,
- restoring required runtime configuration and secrets from the operator's
  secret source,
- verifying `/health/live` and `/health/ready`,
- checking Admin login,
- checking Integration API authentication with an existing credential,
- checking Address Pool counts and assigned address state,
- checking active Payments, Webhook Delivery retry state, and dead-letter
  state,
- checking Blockchain Observation configuration and provider availability,
- confirming that polling through the Integration API remains available as the
  recovery path for missed webhooks.

The baseline does not require a specific backup tool, retention period, or
restore-test frequency. Those choices can be set by the first concrete
operations runbook or deployment profile.

## Logs And Observability

Hosts emit technical logs to `stdout`/`stderr`, preferably structured JSON
suitable for Docker and centralized collectors.

Technical observability follows
[observability-baseline.md](observability-baseline.md): OpenTelemetry/OTLP,
logaffe for logs, with Grafana Alloy and the Grafana LGTM Stack behind traces and metrics, is the target direction.

Minimum production dashboards and alerts should include the shared views for
host/service status, HTTP/API, workers or jobs, database, and error reports.
`payaffe` should add payment-specific views for Blockchain Observation,
Webhook Delivery, payment-state progress, Address Pool capacity, provider
rate limits, and Reorg Alerts once the related signals exist.

Technical logs, traces, metrics, health responses, readiness responses, and
error reports must not contain secrets, bearer tokens, webhook secrets, MFA
material, recovery codes, provider raw secrets, private keys, seed phrases,
full provider payload dumps, or sensitive payment payloads.

## Operations Documentation

Before production use, operations documentation must describe:

- startable Compose profiles or central Compose commands,
- external names and reverse-proxy assumptions,
- required configuration and secret sources,
- the controlled migration command or service,
- the PostgreSQL backup target and backup mechanism,
- the restore procedure,
- health and readiness endpoints,
- required operator-owned assets outside the database backup,
- documented deviations from this baseline.

The documentation should state the behaviour an operator depends on rather
than restating this baseline wholesale.

## Tests

Implementation must include focused verification that:

- health and readiness endpoints return safe responses without secrets,
- readiness fails or refuses traffic when start-critical dependencies are not
  usable,
- readiness reports `not_ready` until the schema this build needs is applied,
- the migration runner handles repeat and concurrent execution safely,
- a host still starts and serves `/health/live` while its database is briefly
  unreachable,
- Compose healthchecks target the intended live or ready signal,
- backup and restore procedures are executable in a local or CI-like
  environment once deployment artifacts exist,
- restored Address Pool state does not reissue assigned native ETH addresses,
- restored Webhook Delivery and durable job state resumes idempotently,
- logs and diagnostics do not leak raw secret values.
