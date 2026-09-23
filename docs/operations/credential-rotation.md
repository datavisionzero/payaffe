# Credential Rotation And Revocation

Raw bearer tokens, Webhook secrets, provider API keys, TOTP secrets, Recovery
Codes, database passwords, and logaffe ingest tokens must never appear in Git,
in logs,
in Audit Log details, in support messages, or in contract snapshots. The product
stores verifiers and restricted references, not secret values, and rotation has
to keep it that way.

## Integration API Credentials

1. Create a replacement Credential in the Admin UI. Creation is CSRF- and
   Step-up-protected and returns the bearer token exactly once.
2. Update the integration and verify both authenticated Payment Creation and
   authenticated polling against the new token.
3. Disable the old Credential.

Disabling takes effect immediately and does not delete its Payments,
idempotency history, Webhook Endpoints, or Audit Log evidence.

There is no overlapping two-token rotation inside a single Credential. A
no-downtime handover uses two Credentials, which is why the steps above create
before disabling.

## Webhook Endpoint Secrets

The product stores a restricted `configuration:Webhooks:EndpointSecrets:<name>`
reference, never the secret itself. Compose ships two slots, `partner-v1` and
`partner-v2`, so a receiver that can verify two keys gets an overlap.

With overlap:

1. Provision the new value in the unused slot.
2. Ask the receiver to accept both keys.
3. Point the Webhook Endpoint at the new reference and restart the affected
   hosts.
4. Send a test event and confirm the receiver validates the signature.
5. Ask the receiver to drop the old key, then clear the old slot.

Without overlap, do the same inside a maintenance window. Events that fail
during the change stay in the outbox and are recoverable by Admin resend, and
receivers can reconcile by polling the Integration API.

## Blockchain Provider Keys

Provider keys are resolved only from
`configuration:BlockchainObservation:ProviderSecrets:<name>`.

1. Provision the replacement under the configured reference.
2. Restart the worker host so it picks up the new value.
3. Confirm Observation Health returns to `available` for every currency.
4. Revoke the old key at the provider.

If compromise is suspected, set `BlockchainObservation:Mode` to `none` first.
Currency Selection then fails closed rather than trusting a provider that may be
answering an attacker.

The Exchange Rate provider key under
`configuration:ExchangeRates:ProviderSecrets:coingecko` rotates the same way.
It is optional; without it the public rate endpoint is used.

## Admin Authentication

For a suspected compromise:

1. Revoke the account's sessions.
2. Change the password through the controlled operator process.
3. Replace the TOTP enrollment, which means provisioning a new secret under
   `Admin:TotpSecrets:<name>` and updating the account's restricted reference.
   Enrolling a factor on an account that had none makes its existing
   password-only sessions step up for every sensitive action, and the first
   sign-in or step-up with the new factor revokes them.
4. Generate new Recovery Codes. Generation revokes every previously active code
   and shows the new ones exactly once.
5. Review the Audit Log for `admin.*` events, for Settlement, Credential,
   Webhook Endpoint, and Address Pool import events, and for entries whose
   source service is `mcp`.

The Admin MCP host acts as one configured Admin Account, distinguished by the
`mcp` source service. Rotating that account's credentials therefore also
governs MCP access; the MCP host itself holds no separate secret beyond the
database connection.

Complete Admin lockout is not recovered by re-running first-Admin bootstrap.
Bootstrap refuses permanently once an Admin Account exists, by design in
[ADR 0020](../adr/0020-the-first-admin-is-created-by-a-local-command.md).

## Database And Telemetry Credentials

Rotate the PostgreSQL password with an operator-approved database procedure,
then update `PAYAFFE_DB_PASSWORD` and restart the migrations, API, worker, and
Admin MCP consumers. All four read the same connection string.

Rotate the OTLP endpoint credentials in restricted configuration and verify
export with a synthetic safe event.

Rotate the logaffe ingest token at the logaffe installation, then update
`PAYAFFE_LOGAFFE_TOKEN` and restart the API, worker, and Admin MCP hosts. The
old token stops being accepted as soon as logaffe revokes it, and entries
produced in between are lost rather than queued — the console log of each host
still has them, which is why the loss is tolerable.

## Record Keeping

Record which credential class was rotated, by whom, and when. Never record the
value. A rotation that cannot be evidenced is indistinguishable from one that
did not happen.
