# Webhook Event Contract

This document records the target contract baseline for outgoing Webhook Events.
Generated schemas, contract snapshots, and tests become the technical source of
truth once implementation exists.

## Scope

The MVP supports outgoing Webhook Events from `payaffe` to Webhook Endpoints
owned by Integration API Credentials. The Payment, credential, endpoint, event,
and Delivery must belong to the same Project; dispatch never fans an event out
across Projects.

The MVP does not support incoming provider webhooks for Blockchain Observation.
Hosted Blockchain API behavior is handled through product-specific observation
adapters and polling unless a later ADR changes that boundary.

Webhook Events are separate from:

- internal domain events,
- transactional outbox rows or jobs,
- Payment Event History,
- Webhook Delivery history,
- security Audit Log entries.

These records should be correlatable but do not replace each other.

## Event Types

The MVP supports these outgoing event types:

| Event Type | Version | Meaning |
| --- | --- | --- |
| `payment.created` | `1` | A Payment was created through the Integration API. |
| `payment.currency_selected` | `1` | The Payer selected a Supported Currency and the Payment was fixed to a cryptocurrency amount and Payment Address. |
| `payment.observed` | `1` | At least one Matching Blockchain Transaction was observed, but completion is not final yet. |
| `payment.completed` | `1` | The Payment met the completion rules and is complete for the external system. |
| `payment.expired` | `1` | The Payment expired without automatic completion. |
| `payment.settled` | `1` | An Admin manually settled the Payment. |

Breaking changes require a new `event_version`, a new event type, or a
documented migration path.

Additive fields are allowed when receivers can safely ignore unknown fields.

The embedded-flow additions are additive within event version `1`:
`expected_crypto_amount_atomic`, `observed_total`,
`confirmed_eligible_total`, and `observed_amount_state` appear when their
values exist. Existing receivers must continue to ignore unknown fields. The
hosted Payer flow and its existing event types remain compatible.

## Event Envelope

Webhook payloads use snake_case field names.

Each payload includes:

- `event_id`: stable identifier for receiver idempotency,
- `event_type`: event type such as `payment.completed`,
- `event_version`: string version, initially `1`,
- `occurred_at`: UTC instant for the Payment lifecycle event,
- `correlation_id`: technical correlation identifier,
- `test_mode`: `true` when the installation is in Test Mode and the event
  describes a simulated Payment, otherwise `false`,
- `resource`: resource reference with `type` and `id`,
- `payment`: data-minimized Payment snapshot for the event.

The Payment snapshot should include only data an external system needs for
reconciliation, such as Payment identifier, External Reference, status, Fiat
Amount, selected Supported Currency when available, expected cryptocurrency
amount when available, its atomic-unit value when available, observed and
confirmed-eligible totals when relevant, observed amount state, Payer Page
reference, Payment Expiration, and settlement or completion timestamps when
relevant.

The existing event set is sufficient for hosted and Embedded Payment Flows.
Transient Payment Option or Currency Selection failures do not emit lifecycle
events because they do not change the Payment. `payment.currency_selected`
confirms that the immutable Rate Lock and Payment Instruction exist; a receiver
that needs the full instruction reconciles through
`GET /api/v1/payments/{paymentId}` rather than relying on delivery order.

An embedded target backend can create, inspect Payment Options, select a
currency, and poll entirely through `/api/v1`. `payer_page_id` and the hosted
Payer Page remain in the contract for compatibility, but consuming either is
optional for an embedded flow. No customer browser request to `payaffe` is
required.

Payloads must not include:

- Integration API bearer tokens,
- webhook secrets,
- provider API keys,
- private keys, seed phrases, or wallet material,
- provider raw payload dumps,
- full internal database rows,
- arbitrary unbounded Payment Context Field dumps beyond the documented
contract.

`test_mode` is the one field a receiver needs to tell a simulated completion
from a real one ([ADR 0033](../adr/0033-a-test-installation-simulates-its-external-truth.md)).
It is always present. A production receiver must reject, or at least never
fulfil against, an event whose `test_mode` is `true`: a test installation can
be configured with the same Webhook Endpoint and a valid secret, so the
signature alone does not prove the money was real.

The v1 envelope does not require `project_id`. Project context is implicit in
the Project-owned Webhook Endpoint, which preserves the existing payload and
signature basis. Admin-facing Delivery views still carry explicit Project
context.

## Headers And Signatures

Outgoing Webhook Deliveries use these headers:

- `Payaffe-Webhook-Id`: Delivery identifier,
- `Payaffe-Webhook-Timestamp`: Unix timestamp in seconds,
- `Payaffe-Webhook-Signature`: signature value,
- `Payaffe-Webhook-Event-Type`: event type,
- `Payaffe-Webhook-Event-Version`: event version.

The signature format is:

```text
v1=<lowercase-hex-hmac-sha256>
```

The canonical signature base is:

```text
<Payaffe-Webhook-Timestamp>.<raw-request-body>
```

The HMAC key is the Webhook Endpoint secret. Receivers should reject signatures
outside a five-minute replay window unless their clock-skew handling
intentionally allows a narrower or wider local window.

Webhook Endpoint records store a secret reference, not the raw secret. The
first production-capable resolver supports references of the form
`configuration:Webhooks:EndpointSecrets:<name>`, which resolves server-side
configuration keys such as `Webhooks__EndpointSecrets__checkout`.
That legacy form belongs to the migrated default Project. A non-default
Project uses
`configuration:Webhooks:Projects:<project-id>:EndpointSecrets:<name>`; the
delivery worker supplies the persisted Project ID and cannot resolve a sibling
Project's namespace.

Webhook secrets are not stored in payloads, Delivery history, logs, traces, or
metrics.

## Delivery Semantics

Delivery is at-least-once, not exactly-once.

Receivers must:

- deduplicate by `event_id`,
- tolerate duplicate deliveries,
- tolerate out-of-order deliveries,
- reconcile by polling `GET /api/v1/payments/{paymentId}` when needed.

Manual resend creates a new Delivery attempt for the same event contract. It
does not create a new Payment lifecycle event.
Admin-triggered manual resend requires server-side Admin authorization, CSRF
evidence for the browser route, a fresh Step-up, and Audit Log recording.

## Retry And Terminal Failure

Webhook Delivery is asynchronous. A Payment state change is durable before
external Webhook Delivery succeeds.

Retryable failures are:

- network connection failure,
- DNS failure,
- timeout,
- HTTP `408`,
- HTTP `425`,
- HTTP `429`,
- HTTP `5xx`.

HTTP `2xx` responses are successful. Other HTTP `3xx` and `4xx` responses are
terminal by default unless a future ADR or implementation note records a
specific exception. Redirects are never followed: a `3xx` is recorded as the
answer and nothing is sent to its `Location`.

The MVP uses exponential backoff with bounded jitter, a configurable maximum
attempt count, and a terminal failed state when automatic retry is exhausted.
The first concrete settings are `Webhooks:Delivery:RetryDelay`,
`RetryBackoffMultiplier`, `MaxRetryDelay`, `RetryJitterRatio`, and
`MaxAttempts`; Compose exposes matching `PAYAFFE_WEBHOOK_DELIVERY_*`
environment variables for operators.

## Delivery Targets

Webhook Delivery connects only to public addresses
([ADR 0036](../adr/0036-webhook-delivery-reaches-only-public-addresses.md)).
Loopback, link-local, private, shared (CGNAT), unspecified, multicast and
reserved addresses are refused unless the target host, its address, or a range
containing it is listed in `Webhooks:Delivery:AllowedPrivateTargets`
(`PAYAFFE_WEBHOOK_ALLOWED_PRIVATE_TARGETS` in Compose). The check applies to the
addresses a host name resolves to when the connection is opened, so a name that
changes what it resolves to is judged at delivery. Delivery does not use an HTTP
proxy.

A refused delivery is a terminal attempt with the safe error code
`webhook_target.not_public` and no HTTP status. Creating or updating a Webhook
Endpoint whose host is a literal non-public address outside the allowlist is
refused with the validation error `webhook_endpoint.target_not_public`. Plain
`http` stays allowed for public targets.

## Delivery History

Webhook Delivery history records at least:

- Delivery identifier,
- event identifier,
- event type and version,
- Webhook Endpoint reference,
- attempt number,
- attempted timestamp,
- result,
- HTTP status or safe error code,
- next retry timestamp or terminal state,
- correlation identifier.

Stored response bodies must be data-minimized. Secrets, tokens, personal data,
provider raw errors with sensitive details, and full payload dumps are not
stored permanently.

## Audit Boundary

Webhook Delivery history is not the security Audit Log.

Audit Log entries are required for:

- creating, disabling, or changing Webhook Endpoints,
- rotating or replacing Webhook Endpoint secrets,
- changing event selection,
- manually resending failed Webhook Deliveries.

Automatic delivery attempts are recorded in Webhook Delivery history and
technical telemetry. They do not require security Audit Log entries by default.

## Observability

Webhook delivery should emit structured logs, traces, and metrics for:

- attempts by event type and result,
- retry count,
- delivery latency,
- failed and terminal deliveries,
- age of the oldest pending delivery,
- signature generation failures,
- endpoint-level failure patterns.

Telemetry must not include webhook secrets, bearer tokens, raw full payloads,
or sensitive endpoint response bodies.

## Contract Checks

Once implementation exists, Webhook Event payloads must be machine-checkable
through JSON Schema, generated snapshots, or an equivalent repository-local
contract mechanism.

CI should fail on unintended breaking changes to event envelope fields, event
versions, signature basis, required headers, or documented enum values.

Each Payment event type has its own accepted payload snapshot under
`docs/contracts/webhooks/`:

- `payment-created.v1.json`
- `payment-currency_selected.v1.json`
- `payment-observed.v1.json`
- `payment-completed.v1.json`
- `payment-expired.v1.json`
- `payment-settled.v1.json`

The snapshots are generated by driving two Payments through the real lifecycle
and delivering the resulting Webhook Outbox events, so a snapshot can only
change when the delivered contract changes. Regenerate them with
`PAYAFFE_UPDATE_CONTRACT_SNAPSHOTS=true dotnet test` and review the diff before
accepting it.

Dynamic UUID values and the generated Payer Page identifier are normalized in
the snapshot test so contract drift focuses on the envelope and the Payment
snapshot shape.

The Payment snapshot carries the Payment state as of delivery, not as of the
event. A Payment that completes before its `payment.observed` event is
delivered therefore reports `completed` in that payload. Receivers must treat
`event_type` as the statement of what happened and the `payment` object as the
current state, and must deduplicate by `event_id`.
