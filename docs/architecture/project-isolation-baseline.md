# Project Isolation Baseline

This document defines how one `payaffe` installation serves one operator with
multiple isolated Projects. It follows
[ADR 0029](../adr/0029-one-operator-can-isolate-payment-projects.md).

## Boundary

A Project is an operator-defined payment-processing boundary inside one
installation. All Admin Accounts are installation-wide and may administer
every Project. Project selection in the Admin UI prevents accidental mixing; it
does not grant or remove Admin permission.

Integration API Credentials, public payment operations, Payer Page operations,
Webhook Delivery, and project-specific background work must resolve exactly one
Project. Missing, disabled, or conflicting Project context fails closed.

Projects have these states:

- `active`: new Payments and normal configuration changes are allowed;
- `disabled`: new Payments and new integration configuration are rejected, but
  existing Payments, Payer Pages, observation, Webhook Delivery, polling, and
  administrative resolution continue;
- `archived`: the Project is hidden from normal active views and read-only.

A Project may be archived only after it has no non-terminal Payments, active
reorg-monitoring windows, or pending or retryable project work. Archiving never
deletes history. An archived Project may be restored to `disabled` by an
audited Admin action when historical remediation is required.

## Resource And Access Matrix

| Resource | Ownership | Required isolation |
| --- | --- | --- |
| Project | Installation | UUID identity; name and stable slug are unique within the installation. |
| Admin Accounts, sessions, MFA and recovery | Installation | Admins can act across Projects; no membership or Project role records exist. |
| Payments and their options, Rate Locks, evidence, event history and Reorg Alerts | Project | Every lookup and mutation uses Project context; child rows preserve the Payment's Project. |
| Integration API Credentials | Project | A bearer token resolves one credential and one Project. Credentials may read all Payments in that Project and none outside it. |
| Idempotency records | Credential within Project | The key remains unique per credential. Project ownership is inherited and enforced by the credential relationship. |
| External References | Project data | Values are not global identifiers and need not be unique. Search and reconciliation never cross Project context unless an Admin explicitly requests an installation-wide view. |
| Watch-Only Wallet Source bindings | Project | A Project binds a configured non-spending source to BTC or LTC; source material stays server-side. Shared address spaces use the installation-wide allocator described below. |
| BTC/LTC derivation allocation and Payment Address assignments | Project assignment, installation-wide address space | Assignments belong to a Project and Payment; cursor and address uniqueness span all Projects. |
| Native ETH Address Pool imports and entries | Project | Imports and capacity are per Project. A normalized network/address pair may appear in only one Project in the installation. |
| Webhook Endpoints | Project through their credential | An endpoint can subscribe only to events for the credential's Project. |
| Webhook Events and Delivery attempts | Project | Events carry Project context from the Payment; dispatch may resolve only endpoints in the same Project. |
| Durable jobs and outbox records | Project or installation | Work against Project-owned data carries `project_id`; genuinely shared schedulers and leases have no Project. |
| Audit Log | Installation with optional Project context | Project actions record the derived `project_id`; installation actions leave it absent. All Admins may search across Projects. |
| Admin MCP calls | Installation-authenticated, explicitly Project-scoped | Tools acting on Project resources require `project_id`; tools return it with resource results. Installation-wide tools either have no Project or accept an explicit filter. |

The Project associated with a Payment is immutable. Moving Payments,
credentials, endpoints, addresses, events, or delivery history between Projects
is not supported. Replacement resources are created in the target Project.

## Configuration Ownership

Installation-wide configuration:

- Admin authentication, sessions, rate limits, and data-protection settings;
- Blockchain Observation Mode, provider endpoints, provider secret references,
  request policy, and Observation Health;
- Exchange Rate Source, source secret reference, Rate Cache interval, maximum
  stale age, request policy, and shared Rate Cache values;
- worker scheduling, leases, batches, retries, and Webhook Delivery retry policy;
- public origins and base URLs, observability, log delivery, and database
  configuration.

Project-owned configuration:

- Project name, slug, status, and enabled Supported Currencies;
- Payment Expiration and Late Acceptance Window;
- Payment Tolerance;
- Confirmation Requirement and Reorg Monitoring Depth per Supported Currency;
- Watch-Only Wallet Source bindings for BTC and LTC;
- native ETH Address Pool entries and low-capacity threshold;
- Integration API Credentials, Webhook Endpoints, event selection, and Webhook
  Endpoint secret references.

At Payment Creation, the Project status, enabled currencies, Payment Expiration,
and Late Acceptance Window determine the created Payment and its absolute time
boundaries. At Supported Currency selection, the Payment snapshots the
Confirmation Requirement, Payment Tolerance, Reorg Monitoring Depth, selected
address-source binding, and Rate Lock evidence. Later Project configuration
changes apply only to later Payments or selections. Historical decisions remain
explainable from stored snapshots.

Provider and exchange-rate secrets are installation-wide. Project-owned secret
references, including Webhook Endpoint secrets and Watch-Only Wallet Source
references, are resolved with the owning Project identifier and cannot name a
different Project's namespace. Raw secret values and extended public keys do
not enter browser state, API responses, logs, Audit Log entries, or snapshots.

## Storage Enforcement

All Project-owned tables contain a non-null `project_id` foreign key. Long-lived
resources keep UUID identifiers, and references between Project-owned tables
use composite candidate keys and foreign keys containing both `project_id` and
the resource ID. A Webhook Endpoint cannot therefore reference a credential in
another Project, and a Webhook Event or job cannot reference a Payment in
another Project even when application validation is bypassed.

Indexes for Project-scoped queries begin with `project_id`. Uniqueness that is
meaningful only inside a Project includes `project_id`; installation safety
constraints such as normalized Payment Address uniqueness remain global.

The application has two explicit access paths:

- Project-scoped stores require a non-empty Project context and apply it to
  every query and mutation. The ordinary Integration API, Payer Page, Webhook,
  and project-worker paths cannot issue an unscoped query.
- Platform stores are separate interfaces used only by authenticated Admin
  operations and installation schedulers that intentionally cross Projects.

EF Core query filters may provide defense in depth, but they are not the sole
enforcement. Raw SQL requires an explicit Project parameter or a separately
reviewed platform path. Contract and integration tests exercise cross-Project
identifier substitution for every public, Payer, Admin, MCP, worker, and
Webhook operation.

PostgreSQL row-level security and a database or schema per Project are not part
of this baseline. All hosts use shared database infrastructure, and Admin and
scheduler workloads legitimately cross Projects. The chosen composite
constraints plus separate scoped/platform stores keep that distinction visible
in code and testable without pooled-session database state.

## Address Collision Prevention

BTC and LTC use an installation-wide address-space registry. Its identity is a
fingerprint of the canonical Supported Currency, network, address type or
derivation branch, and Watch-Only Wallet Source. Project bindings that resolve
to the same fingerprint share one cursor. The first binding establishes the
starting index; later bindings cannot rewind or replace it.

Address allocation atomically advances that cursor and creates a Project-owned
assignment in the same transaction. A global unique constraint on normalized
Supported Currency, network, and Payment Address is the final guard. An
allocation conflict is retried with the next index and never repaired by
reassigning an existing address.

Native ETH imports are Project-owned, but normalized network/address uniqueness
is installation-wide. Assigned native ETH addresses are never recycled or
moved to another Project. Restores must preserve the registry, cursors, pool
ownership, and all assignment history.

## Disabled And Archived Projects

Disabling a Project is a circuit breaker for new business, not cancellation of
accepted work:

- `POST /api/v1/payments` is rejected with `project.disabled`;
- existing credentials remain resolvable for polling existing Payments;
- already-created Payer Pages remain usable, including Supported Currency
  selection, until their stored expiration and Late Acceptance Window rules end;
- the authenticated Integration API may perform the same Supported Currency
  selection for an existing Payment under its owning Project;
- observation, confirmation, reorg monitoring, expiration, settlement,
  Webhook Delivery, retries, and manual resend continue;
- Admins may inspect and resolve existing work, import native ETH addresses
  needed by an existing Payer Page, and re-enable the Project;
- new credentials, endpoints, and unrelated address imports are rejected.

Workers use the Payment's stored policy and Project context rather than the
Project's current active state. Disabling or archiving a Project does not cancel
already durable work. Archival waits until that work has reached a terminal
state.

## Public Contract Compatibility

The existing `/api/v1` request and response shapes, Payment identifiers, Payer
Page identifiers and URLs, bearer tokens, idempotency keys, External
References, Webhook Event IDs and versions, and signature basis remain valid.
The bearer token supplies Project context, so callers do not add a Project
header or path segment.

Existing credentials all belong to the migrated default Project. Because a
credential may inspect every Payment in its Project, an integration that used
one existing credential to poll a Payment created by another remains valid.
Project isolation therefore tightens access only between newly distinct
Projects and does not require `/api/v2`.

Additive responses may expose `project_id` only where it helps an Admin or a
future multi-Project integration. Webhook v1 payloads do not require it because
the endpoint is already Project-owned. A future endpoint that lets one
credential span Projects, or a request-selected Project header, is a breaking
authorization change and requires a new API contract decision.

## Upgrade And Default Project

The first multi-Project migration creates exactly one active default Project
for an existing installation and maps all existing Project-owned records to it.
The default Project receives all existing Integration API Credentials,
Payments and children, idempotency records, wallet cursors and assignments,
native ETH pool data, Webhook Endpoints, events and deliveries, Reorg Alerts,
and project-specific jobs.

Current environment-backed Project settings cannot be read by a SQL migration.
Before readiness, a one-time, advisory-lock-protected upgrader materializes the
effective legacy Payment, confirmation, tolerance, reorg, address-source, and
capacity settings as default-Project configuration. A second host validates the
same effective values and fails readiness on disagreement rather than silently
choosing one. Installation-wide settings remain in runtime configuration.

The upgrade must preserve these invariants:

- row counts and identifiers are unchanged except for new Project and
  configuration records;
- every Project-owned row has the default `project_id`, and every composite
  relationship points to the same Project;
- existing token hashes, bearer tokens, Payment and Payer Page identifiers,
  URLs, idempotency behavior, Webhook Event IDs, payload versions, delivery
  attempts, and signature inputs remain unchanged;
- existing absolute expiration, late-acceptance, completion, settlement, job,
  lease, retry, and observation timestamps remain unchanged;
- address cursors never move backwards, assigned addresses never become
  available, and existing BTC, LTC, and native ETH addresses remain globally
  unique;
- non-terminal Payments receive policy snapshots matching the effective legacy
  configuration before new Project configuration can be changed;
- no migration-generated Payment event or external Webhook Delivery is emitted;
- one Audit Log entry records successful default-Project creation and settings
  migration without raw configuration or secret values.

The migration validates these invariants before applying non-null and composite
constraints. Conflicts stop the upgrade before readiness; they are not resolved
by deleting, moving, or renumbering product data.

## Threat Cases

| Threat | Required control |
| --- | --- |
| A credential guesses a Payment ID from another Project. | Resolve Project from the token and query by both `project_id` and Payment ID; return the same safe not-found response used for an unknown Payment. |
| A handler omits Project filtering. | Project-scoped stores fail without context, apply mandatory filters, and have cross-Project substitution tests; platform stores are separate and unavailable to Integration API handlers. |
| An endpoint or job is attached to a resource in another Project. | Composite Project-preserving foreign keys reject the write. |
| Two Projects configure the same BTC or LTC source. | Both bindings resolve to one installation-wide address-space cursor and global address uniqueness. |
| The same native ETH address is imported twice. | Installation-wide normalized network/address uniqueness rejects the second import, regardless of Project. |
| A Project secret reference points into another Project. | The resolver receives the owner Project ID and accepts only that Project's namespace. |
| An Admin changes the selected Project in browser state. | The server derives resource ownership from persisted IDs and treats selection only as explicit request context; it never trusts a hidden UI filter as authorization. |
| A Project is disabled while Payments are active. | New creation stops, while stored policy and durable work continue until terminal states. |
| An upgrade leaves unowned or mixed records. | Readiness stays unavailable until backfill, configuration import, validation, and constraints complete. |

## Required Verification

Implementation must include focused tests for:

- credential authentication resolving exactly one Project;
- cross-Project Payment reads and mutations failing safely;
- Project-preserving database constraints for every relationship;
- idempotency remaining credential-scoped inside a Project;
- shared BTC/LTC sources allocating distinct addresses under concurrency;
- native ETH duplicate import across Projects;
- Webhook Events dispatching only to same-Project endpoints;
- project jobs loading the correct configuration and policy snapshot;
- disabled-Project creation rejection and in-flight completion;
- archive preconditions and historical retention;
- default-Project migration invariants and repeatable startup validation;
- Admin UI and MCP results always carrying or visibly selecting Project context;
- no secret, extended public key, or cross-Project data exposure in responses,
  logs, Audit Log entries, metrics, traces, or contract snapshots.
