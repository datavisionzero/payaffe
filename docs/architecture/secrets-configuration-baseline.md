# Secrets And Configuration Baseline

This document records the target baseline for runtime configuration and
secrets in `payaffe`.

## Scope

The baseline covers:

- runtime configuration,
- local and production `.env` handling,
- server, browser, and build-time boundaries,
- startup validation,
- secret storage expectations,
- rotation and revocation expectations,
- logging, Audit Log, observability, and diagnostic boundaries.

It does not choose a mandatory central secret store.

## Secret Classes

`payaffe` must treat these values as secrets when they exist:

- PostgreSQL credentials and connection strings with credentials,
- Admin password hashes and password reset material,
- Admin TOTP MFA secrets,
- Admin recovery codes,
- session, cookie, CSRF, antiforgery, encryption, and data-protection keys,
- Integration API bearer tokens,
- Webhook Endpoint secrets,
- Hosted Blockchain API provider keys,
- Exchange Rate Source API keys,
- logaffe ingest tokens,
- GlitchTip or Sentry-compatible DSNs when an installation treats them as
  sensitive,
- OTLP exporter credentials or headers,
- SMTP credentials if email is added later,
- Registry credentials, deploy keys, SSH keys, or CI/CD credentials.

`payaffe` must not store spending keys, seed phrases, private keys, keystores,
or wallet recovery phrases. Public receiving addresses are not secrets, but
address-pool exports can still be operationally sensitive.

## Versioning And Local Files

Real secrets are not versioned.

The repository ignores:

- `.env`,
- `.env.*`.

The repository may version `.env.example` once concrete configuration names
exist. Example files must use safe placeholders or local-only non-production
defaults. They must not include real production values, provider credentials,
personal data, real connection strings, tokens, passwords, private keys, or
seed phrases.

## Runtime Configuration

Runtime configuration is separate from build artifacts and container images.

The self-hosting minimum is server-side configuration through environment
variables and non-versioned `.env` files with restrictive filesystem
permissions.

Allowed alternatives include:

- CI/CD secrets,
- SOPS/age-managed files,
- systemd environment files,
- Docker secrets,
- operator-provided environment variables,
- a dedicated secret store.

The selected mechanism for a production installation must be documented in
operations documentation.

## Server, Browser, And Build Boundaries

Secrets stay server-side.

Browser-readable configuration is public. `NEXT_PUBLIC_*`, static assets,
browser logs, service workers, and client bundles must not contain secrets.

Build arguments and image layers are not a safe place for secrets. Productive
secrets must not be required to build the web frontend or backend container
images.

## Startup Validation

Hosts must validate required configuration at startup.

Start-critical configuration includes at least:

- database connection configuration,
- session and data-protection configuration,
- required Admin authentication protection secrets,
- required encryption or signing keys when used by the host.

If start-critical production configuration is missing or invalid, the host must
fail fast or refuse readiness.

Optional integrations may be disabled when their secrets are missing:

- Hosted Blockchain API provider mode that requires a provider key,
- CoinGecko or another Exchange Rate Source mode that requires an API key,
- log delivery to logaffe,
- GlitchTip or OTLP export,
- SMTP if email is added later.

Disabled optional integrations must be visible through configuration
diagnostics, readiness where relevant, Admin UI status, or operations
documentation without exposing secret values.

## Storage Rules

Plaintext values shown once:

- Integration API bearer token at creation,
- Admin recovery code at generation.

Server-side stored values must use protected hashes or equivalent
non-reversible representations when verification is sufficient:

- Integration API bearer tokens,
- Admin passwords,
- recovery codes.

Server-side stored values that must be reused to authenticate outbound requests
or sign data remain secrets at rest and in memory:

- Webhook Endpoint secrets,
- Hosted Blockchain API provider keys,
- Exchange Rate Source API keys,
- session, cookie, CSRF, antiforgery, encryption, and data-protection keys,
- observability exporter credentials.

Webhook Endpoint secrets are needed for HMAC signing and therefore cannot be
stored only as hashes unless a future design introduces an external signing or
secret mechanism.
The first concrete Webhook Endpoint secret resolution path stores only a
secret reference on the Webhook Endpoint record. References in the form
`configuration:Webhooks:EndpointSecrets:<name>` resolve to server-side
configuration values under `Webhooks:EndpointSecrets:<name>`.

## Rotation And Revocation

Before production use, operations documentation must explain rotation and
revocation for:

- Integration API bearer tokens,
- Webhook Endpoint secrets,
- Hosted Blockchain API provider keys,
- Exchange Rate Source API keys,
- session, cookie, CSRF, antiforgery, encryption, and data-protection keys,
- Admin recovery codes,
- observability credentials,
- database credentials.

Manual rotation is acceptable for the MVP when it documents:

- who performs the rotation,
- where the new secret is configured,
- whether old and new values overlap,
- how old values are revoked,
- which clients or external systems must be updated,
- expected downtime or retry behavior,
- required Audit Log entries.

Zero-downtime or overlapping-key rotation should be added when a secret class
has external clients that cannot all be updated atomically.

## Logging, Audit, And Diagnostics

Secret raw values must not appear in:

- technical logs,
- traces,
- metrics,
- GlitchTip or error reports,
- health or readiness responses,
- Audit Log entries,
- Payment Event History,
- Webhook Delivery history,
- OpenAPI examples,
- Webhook contract snapshots,
- MCP contract snapshots,
- screenshots or documentation examples.

Safe diagnostics may include:

- configuration key name,
- secret class,
- key identifier without the secret value,
- created or rotated timestamp,
- validation result,
- correlation identifier,
- affected host or environment.

Security-relevant secret and configuration changes are Audit Log events. Audit
Log entries must describe actor, subject, outcome, and reason code without
storing secret raw values.

## Tests

Implementation must include focused verification that:

- required production configuration is validated,
- missing start-critical secrets fail fast or fail readiness,
- missing optional provider keys disable the related integration safely,
- local development defaults are not accepted as production secrets,
- browser bundles and `NEXT_PUBLIC_*` values do not contain server secrets,
- logs, errors, health, readiness, Audit Log entries, Webhook Delivery history,
  OpenAPI examples, and snapshots do not contain secret raw values,
- token and recovery-code storage uses protected hashes,
- rotation and revocation work for the changed secret class.
