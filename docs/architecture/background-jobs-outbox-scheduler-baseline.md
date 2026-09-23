# Background Jobs, Outbox, And Scheduler Baseline

This document records the target baseline for durable background work in
`payaffe`.

## Scope

Durable background work includes:

- Blockchain Observation polling,
- confirmation tracking,
- reorg monitoring,
- Payment Expiration processing,
- Late Acceptance Window processing,
- outgoing Webhook Event delivery,
- failed Webhook Delivery retry and manual resend processing,
- exchange-rate refresh,
- Address Pool low-capacity checks,
- cleanup and retention work,
- dead-letter review and recovery work.

The MVP starts with .NET workers and PostgreSQL-backed state.

## Durable Work Types

Expected durable work includes:

| Work Type | Purpose |
| --- | --- |
| `blockchain_observation_poll` | Poll active Payment Addresses for the selected Blockchain Observation Mode. |
| `confirmation_update` | Refresh confirmation counts for observed Matching Blockchain Transactions. |
| `reorg_monitoring` | Continue post-completion monitoring up to Reorg Monitoring Depth. |
| `payment_expiration` | Mark Payments expired: Pending Currency Selection at Payment Expiration, Waiting For Payment when the Late Acceptance Window ends, and an Observed Payment when its Confirmation Wait ends ([ADR 0034](../adr/0034-an-observed-payment-waits-for-the-confirmations-of-a-transaction-it-saw-in-time.md)). |
| `late_acceptance_check` | Process late Matching Blockchain Transactions inside the Late Acceptance Window. |
| `rate_refresh` | Refresh Rate Cache entries for Supported Currencies. |
| `webhook_delivery` | Deliver outgoing Webhook Events to Webhook Endpoints. |
| `webhook_manual_resend` | Re-deliver a failed event after an Admin request. |
| `address_pool_capacity_check` | Detect low native ETH Address Pool capacity. |
| `retention_cleanup` | Apply documented cleanup and retention rules. |

Work type names are stable internal names, not public API contracts.

## Transaction Boundaries

When a command changes Payment state, the same database transaction should
persist all required durable records for that command:

- Payment row changes,
- Matching Blockchain Transaction records,
- Payment Event History entries,
- Webhook Event outbox records,
- required Audit Log entries,
- follow-up jobs that are part of the accepted command.

Examples:

- Payment Creation persists the Payment, idempotency evidence, Payment Event
  History entry, and `payment.created` Webhook Event outbox record together.
- Currency selection persists the Rate Lock, assigned Payment Address, Payment
  Event History entry, and `payment.currency_selected` Webhook Event outbox
  record together.
- Payment completion persists completion state, Payment Event History entry,
  `payment.completed` Webhook Event outbox record, and required follow-up work
  together.
- Manual Settlement persists the settlement, Payment Event History entry,
  `payment.settled` Webhook Event outbox record, and Audit Log entry together.

External calls are not made inside long database transactions. Workers call
Hosted Blockchain APIs, Exchange Rate Sources, and Webhook Endpoints after
durable work has been recorded.

## Job And Outbox Records

Durable job or outbox records need at least these concepts:

- stable identifier,
- work type,
- status,
- payload version,
- safe payload or resource reference,
- created timestamp,
- due or next-attempt timestamp,
- attempt count,
- lock owner or lease information,
- safe error code or reason code,
- correlation identifier,
- Project context for Project-owned work, or installation context for shared
  work.

Payloads should reference product data by ID instead of copying full domain
payloads. They must not contain secrets, bearer tokens, webhook secrets, MFA
secrets, recovery codes, provider raw secrets, full provider payload dumps, or
private wallet material.

## Status Model

The implementation status model must express at least:

- pending or due,
- claimed or running,
- retry pending,
- completed,
- failed transiently,
- terminal failed or dead-lettered,
- cancelled or discarded when a documented operator action permits it.

Status names may differ in code, but the states must be observable.

## Idempotency And Deduplication

Worker processing must be idempotent or protected by stable deduplication and
state guards.

Required deduplication or idempotency examples:

- Payment Creation uses Integration API Credential plus `Idempotency-Key`.
- Matching Blockchain Transactions are deduplicated per Payment by Supported
  Currency and transaction hash or another provider-independent stable
  transaction identity. One Blockchain Transaction that pays several Payment
  Addresses is a Matching Blockchain Transaction of each of those Payments.
- Webhook receivers deduplicate by `event_id`; `payaffe` must not create
  multiple lifecycle events for one state transition.
- Manual Webhook resend reuses the same event contract and does not create a
  new Payment lifecycle event.
- Re-running an expiration, completion, or settlement job must become a safe
  no-op when the Payment has already moved past that state.

## Claiming And Locking

Workers must claim due work atomically.

Suitable PostgreSQL mechanisms include:

- `SELECT ... FOR UPDATE SKIP LOCKED`,
- atomic `UPDATE ... WHERE` claims,
- lease columns such as `locked_by` and `locked_until`,
- advisory locks for scheduler or singleton work when justified.

Locks must expire or be recoverable. Abandoned work must become visible and
eligible for retry or operator diagnosis.

A worker whose lease expired must not write the item it claimed: every write
after a claim is conditional on still owning the lease. A lease that can expire
during a single unit of work, such as one outbound request, is a
misconfiguration and is refused at startup.

One item that fails unexpectedly must not stop the batch. It is recorded as a
failed attempt with backoff and becomes terminal when its attempts run out.

Parallel work must respect domain exclusivity. Two workers must not complete,
expire, or settle the same Payment concurrently without optimistic concurrency
or an equivalent state guard.

## Retry, Backoff, And Dead Letter

Retry behavior must distinguish transient and terminal failures.

Retry records include:

- attempt count,
- next attempt timestamp,
- safe error or reason code,
- correlation identifier.

Retryable examples:

- temporary Hosted Blockchain API outage,
- rate-limit response from a provider,
- temporary Exchange Rate Source outage,
- retryable Webhook Delivery failure,
- database serialization or lock timeout where retry is safe.

Terminal or dead-letter examples:

- malformed stored job payload,
- unsupported work type or payload version,
- permanent Webhook Endpoint rejection after policy-defined attempts,
- provider response that proves the requested operation is invalid,
- repeated failure after configured maximum attempts.

Dead-lettered records must not disappear silently. Operators need an Admin UI,
MCP tool, operations command, or documented database procedure for analysis and
safe recovery once real production work exists.

## Scheduler

Schedulers persist their state or persist due work as job records.

Critical schedulers include:

- Blockchain Observation polling per Supported Currency and selected
  Blockchain Observation Mode,
- Rate Cache refresh,
- Payment Expiration processing,
- Late Acceptance Window checks,
- confirmation updates,
- reorg monitoring,
- Webhook Delivery retry,
- retention cleanup.

Scheduler behavior must define whether missed runs catch up, skip, or coalesce.
Only one active run per singleton scheduler type should execute unless a future
implementation deliberately shards the work.

In-memory timers without persistent state are allowed only for non-critical,
loss-tolerant, process-local work.

## Project And Installation Context

Jobs and outbox records that act on Project-owned resources carry a non-null
`project_id`. The Project is copied from the source resource when work is made
durable; workers do not infer it from mutable process state or a currently
selected Admin view.

Installation-wide schedulers and leases may have no Project. They enumerate
eligible Projects explicitly and create or claim Project-scoped work before
loading Project-owned configuration. Shared Blockchain Observation provider
configuration, Exchange Rate Source and cache policy, rate limits, and
observability remain installation context.

Disabling a Project prevents new Payment Creation but does not cancel durable
work for existing Payments. Project workers continue with stored Payment policy
until the work reaches a terminal state. Archival is rejected while Project
work remains pending or retryable.

## Audit Boundary

Worker execution is not automatically a security Audit Log event.

Audit Log entries are required when background work completes or records
security-relevant actions, including:

- manual Settlement performed through Admin UI or admin MCP,
- manual Webhook Delivery resend,
- security-relevant configuration changes processed asynchronously,
- risky admin MCP writes processed asynchronously,
- Audit Log export work if it is asynchronous.

Technical delivery attempts, observation polls, rate refreshes, and retries are
recorded in product histories, delivery histories, job state, and observability
unless they have security-relevant meaning.

## Observability

Workers should emit structured logs, traces, and metrics for:

- processed work by type and result,
- retry count,
- dead-letter count,
- processing duration,
- oldest due work age,
- queue or outbox depth,
- provider request result by provider and Supported Currency,
- Webhook Delivery result by event type and endpoint,
- active worker instances and worker health.

Telemetry must not include secrets, full provider payloads, full domain
payloads, bearer tokens, webhook secrets, private keys, seed phrases, or raw
recovery material.

## Tests

Implementation must include focused tests for:

- outbox/job creation in the same transaction as the related state change,
- idempotent worker retry,
- deduplication of Matching Blockchain Transactions,
- safe no-op behavior for already-applied Payment transitions,
- PostgreSQL claiming and lock expiry under parallel workers,
- retry and dead-letter behavior,
- scheduler state persistence,
- safe payload contents without secret raw values,
- correct Project propagation and cross-Project claim isolation,
- disabled Projects completing already durable work,
- observability and Audit Log expectations for security-relevant work.

PostgreSQL integration tests with Testcontainers are the preferred direction
for transaction boundaries, locking, concurrency, and retry behavior.
