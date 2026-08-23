# Database Conventions Baseline

This document records the target PostgreSQL conventions for `payaffe`.

## Naming

Database objects use lowercase `snake_case`:

- schemas,
- tables,
- columns,
- indexes,
- constraints,
- functions,
- views.

Quoted mixed-case identifiers are not the standard path.

Foreign-key columns should name the referenced domain concept, such as
`payment_id`, `admin_account_id`, `integration_api_credential_id`, or
`webhook_endpoint_id`.

## IDs

Long-lived domain and contract-visible IDs use UUIDs.

UUID IDs are expected for at least:

- Payments,
- Admin Accounts,
- Integration API Credentials,
- Webhook Endpoints,
- Webhook Events and Deliveries,
- Matching Blockchain Transactions,
- Payment Event History entries,
- Address Pool entries and imports,
- Rate Locks,
- Audit Log events,
- durable jobs or outbox records when externally referenced.

Technical attempt numbers, import row numbers, outbox ordering values,
scheduler positions, and migration metadata may use other types when they are
not stable domain identifiers.

## Time Values

Technical timestamps are UTC instants stored as `timestamptz`.

This includes:

- `created_at`,
- `updated_at`,
- `occurred_at`,
- `observed_at`,
- `first_observed_at`,
- `last_checked_at`,
- `expires_at`,
- `next_retry_at`,
- `locked_until`,
- `completed_at`,
- `settled_at`.

Date-only values use PostgreSQL `date` only when no instant is meant.

`timestamp without time zone` is not used for technical instants.

## Technical Columns

Mutable domain tables should include:

- `created_at`,
- `updated_at`,
- `version` when concurrent updates can lose data or create incorrect states.

Append-only tables and histories may use purpose-specific columns such as
`occurred_at` instead of `updated_at` when records are not updated during
normal product operation.

Outbox, job, scheduler, migration, and audit tables may use different
technical columns when their meaning is explicit.

API `ETag` values may be derived from `version`, but the database value is not
exposed as a parseable API contract.

## Schemas

Application tables are not placed in PostgreSQL `public`.

Initial schemas:

| Schema | Purpose |
| --- | --- |
| `app` | Payment-domain data and product configuration. |
| `auth` | Local Admin Account authentication, Admin sessions, MFA recovery material, and Integration API Credential authentication data. |
| `audit` | Security-relevant Audit Log entries. |
| `outbox` | Transactional Outbox records, Webhook Delivery processing, durable job state, scheduler state, and dead-letter records. |

Expected `app` data includes:

- Payments,
- Matching Blockchain Transactions,
- Rate Locks,
- Payment Event History,
- Address Pool entries and imports,
- Webhook Endpoint product configuration,
- product configuration values that are not authentication secrets.

Expected `auth` data includes:

- Admin Account authentication records,
- Admin password hashes,
- Admin MFA metadata and recovery-code hashes,
- Admin sessions,
- Integration API Credential token hashes and authentication status.

Expected `audit` data includes:

- security-relevant Audit Log entries,
- Audit export events.

Expected `outbox` data includes:

- outgoing Webhook Event outbox records,
- Webhook Delivery attempts and terminal state,
- durable background jobs,
- scheduler and lock state,
- dead-letter records.

## Migrations

Schema changes are versioned migrations in the repository once implementation
exists.

EF Core is the default migration tool. Migrations must set schemas and names
explicitly enough to preserve the conventions in this document.

Explicit SQL is allowed for:

- payment matching queries,
- blockchain observation projections,
- outbox and job claiming,
- lock and concurrency behavior,
- reporting,
- PostgreSQL-specific indexes or constraints.

Production migrations must run through one controlled path:

- dedicated migration runner,
- explicit deployment step,
- documented operations command.

Production migrations must not run as an unordered side effect of normal
`web`, `api`, `mcp`, or `worker` host startup.

A controlled migration run must:

- make failure visible,
- avoid or safely handle concurrent migration execution,
- be repeatable where the migration tool supports it,
- leave enough logs for operators without exposing secrets.

Local development and automated tests may initialize or migrate databases more
conveniently, as long as that behavior is not documented as the production
rule.

## Tests

Implementation must include focused verification that:

- generated migrations use the expected schemas,
- database names are lower `snake_case`,
- domain IDs use UUIDs where required,
- technical instants use `timestamptz`,
- mutable concurrent resources have a concurrency value where needed,
- concurrent hosts applying the schema on startup do so exactly once,
- explicit SQL paths preserve authorization, audit, tenant or installation
  context, and observability expectations.
