# Incident Response

## Severity And Ownership

The on-call operator becomes incident commander, records a UTC timeline with
correlation identifiers, and preserves Audit Log entries and telemetry as
evidence before changing anything.

Treat as high severity:

- uncertainty about Payment integrity or Settlement,
- disclosure of an Integration API token, Webhook secret, provider API key,
  Extended Public Key, or Admin credential,
- gaps or unexplained entries in the Audit Log,
- database loss or a restore from an old backup,
- unauthorized Admin or Admin MCP actions.

## First Assessment

```sh
# Is anything running, and is it healthy?
docker compose ps
curl -fsS http://localhost:8080/health/ready

# What are the workers doing?
docker compose exec -T db psql --username "$PAYAFFE_DB_USER" --dbname "$PAYAFFE_DB_NAME" -c \
  "select worker_name, locked_by, locked_until, last_failed_at, last_safe_error_code, consecutive_failure_count from app.background_worker_leases order by worker_name"

# What is stuck?
docker compose exec -T db psql --username "$PAYAFFE_DB_USER" --dbname "$PAYAFFE_DB_NAME" -c \
  "select status, count(*) from outbox.webhook_events group by status"
```

The Admin UI shows Payments, Reorg Alerts, Observation Health, Webhook Delivery
history, and the Audit Log for the same questions without database access.

## Containment

- **Leaked Integration API Credential.** Disable it in the Admin UI. Disabling
  is immediate and keeps its Payments, idempotency history, Webhook Endpoints,
  and Audit Log evidence intact. Issue a replacement Credential for the
  integration.
- **Leaked Webhook Endpoint secret.** Rotate the secret value in the operator
  secret source and coordinate the verification-key change with every receiver
  before delivery resumes. See [credential-rotation.md](credential-rotation.md).
- **Leaked provider API key.** Set `BlockchainObservation:Mode` to `none` first
  if abuse is suspected, so Currency Selection fails closed instead of trusting
  a compromised provider. Then rotate the key and restore the mode.
- **Compromised Admin Account.** Revoke its sessions, replace the password and
  TOTP enrollment, and generate new Recovery Codes. Generation revokes all
  previously active codes. Review Audit Log entries for `admin.*` events and for
  entries whose source service is `mcp`.
- **Uncertain worker behaviour.** Stop the worker service rather than editing
  Payment records:

  ```sh
  docker compose stop worker
  ```

  Payment expiration, Blockchain Observation, Reorg Monitoring, Rate Cache
  refresh, and Webhook Delivery all pause. The API keeps serving requests, and
  integrations keep working by polling. Nothing is lost: the outbox and the
  observation targets are durable and resume when the worker starts again.
- **Suspected data leakage.** Restrict access, preserve evidence, and follow the
  operator's legal and communication process.

Never rewrite Payment status, Payment Event History, or Audit Log rows by hand.
Those records are the evidence the incident is reconstructed from, and manual
edits also break the optimistic concurrency the product relies on.

## Specific Situations

### A worker keeps failing

`consecutive_failure_count` at five or more means the same batch failed five
times in a row. Read `last_safe_error_code`, then check the worker logs for the
correlated exception. A lease is released on every failure, so a second instance
can take over; a persistently failing worker is a code or dependency problem,
not a stuck lease.

A lease whose `locked_until` is in the past belongs to a crashed instance. It is
taken over automatically on the next poll; no manual cleanup is needed.

### A Reorg Alert is open

A completed Payment lost confirmations. Completed Payments stay final by design,
so this needs a human decision: accept the loss, or contact the Payer. Record
the decision, then resolve the alert. Do not reopen or re-run the Payment.

### Blockchain Observation is unavailable

Observation Health per currency shows `unavailable` with the last safe error
code. Payments in that currency cannot be detected while it lasts. Check the
provider status and the API key, and consider switching `Mode` to the other
supported provider. Late Acceptance means Payments observed after expiry can
still be accepted, so a bounded outage does not automatically lose Payments.

### The native ETH Address Pool is empty

Native ETH cannot be selected without a free address. Import more addresses
following [ethereum-address-pool.md](ethereum-address-pool.md). Never return an
`assigned` address to `unused`.

### Webhook Deliveries are terminal-failed

Terminal failures wait for an operator. Once the receiver is healthy, resend
from the Admin UI or the Admin MCP; resend reuses the same event contract and
signing path and records a new delivery attempt. Receivers can also recover
missed outcomes by polling the Integration API.

## Recovery

Before closing, confirm:

- readiness succeeds and every expected `service.name` reports telemetry,
- worker leases advance and failure counts are back to zero,
- Observation Health is `available` for every configured currency,
- address derivation cursors and Address Pool state are intact,
- no Reorg Alert is open without a recorded decision,
- pending and terminal Webhook Deliveries are drained or accounted for,
- the Audit Log is continuous across the incident window.

Replay only supported idempotent workflows through the Admin UI or the Admin MCP
confirmation boundary.

Close the incident only after credentials are rotated or revoked, monitoring is
normal, affected integrations are informed, and follow-up actions have owners
and dates. Publish a blameless post-incident review for high-severity incidents.
