# Admin Security Baseline

This document records the target security baseline for local Admin Accounts,
Admin UI access, and admin-authenticated operations.

## Scope

The MVP Admin surface includes:

- Admin UI browser access,
- admin MCP actions,
- Admin Account authentication and sessions,
- security-relevant Audit Log entries.

The Integration API is authenticated separately with Integration API
Credentials and static bearer tokens.

## Admin Accounts

The MVP supports multiple local Admin Accounts.

Each Admin Account is a privileged product-local actor. The MVP has one Admin
permission level, so all Admin Accounts can perform the same Admin actions
after authentication, authorization, and any required step-up.

Admin Accounts authenticate with:

- local password,
- optionally TOTP MFA, enrolled by the admin (ADR 0028),
- product-local session cookie.

Admin password hashes, MFA secrets, recovery codes, session identifiers, and
CSRF tokens are secrets. They must not appear in Audit Log entries, technical
logs, traces, metrics, error responses, or exported diagnostics.

## Sessions

Admin sessions are represented by product-local cookies with:

- `HttpOnly`,
- `Secure`,
- `SameSite=Lax`.

Session limits:

- absolute maximum duration: 7 days,
- inactivity limit: 12 hours.

The MVP does not provide Remembered Devices or `remember me`.

Product logout ends the local `payaffe` Admin session. There is no central or
federated logout, because there is no external identity provider.

## CSRF

Unsafe cookie-authenticated Admin browser requests require CSRF evidence.

Unsafe methods are at least:

- `POST`,
- `PUT`,
- `PATCH`,
- `DELETE`.

The standard request header is:

```text
X-CSRF-TOKEN
```

ASP.NET Core antiforgery is the standard validation mechanism. Next.js App
Router remains the UI and routing layer; protected product mutations must go
through a backend path that validates the Admin session, CSRF evidence, and
authorization.

`GET`, `HEAD`, and `OPTIONS` must not have security- or payment-relevant side
effects.

Bearer-token Integration API requests and non-browser admin MCP requests do not
use cookie CSRF tokens.

## Authorization

Authorization is enforced server-side.

The MVP policy model is intentionally small:

- `Admin`: authenticated local Admin Account.

Because all Admin Accounts are privileged, the policy question is not which
role they have, but whether the actor, session, MFA state, step-up state, and
target action are valid.

Protected actions fail closed when any required context is missing or invalid:

- Admin Account identity,
- active session or admin MCP authentication,
- MFA state,
- step-up state when required,
- CSRF evidence for unsafe Admin browser requests,
- target resource existence,
- product rule allowing the action.

Frontend route guards, hidden buttons, disabled controls, and local state are
only UX. They do not authorize protected effects.

## Step-Up

All Admin Accounts are MFA-capable. MFA is not required: an account without an
enrolled second factor signs in on its password and may perform every
operation, and step-up is enforced only for accounts that have one
([ADR 0028](../adr/0028-the-second-factor-is-optional-and-enrolled-later.md)).
A session records honestly what it cleared — the MFA and step-up timestamps are
null when no factor was ever verified.

For an account with an enrolled second factor, particularly sensitive actions
require a fresh step-up if the current MFA-backed authentication is older than
15 minutes. An account without one is not asked: it has nothing to step up
with, and refusing would make these actions unreachable rather than protected.
The list below is what step-up covers where it applies.

Step-up-required actions include:

- creating or disabling Integration API Credentials,
- creating, disabling, changing, or rotating Webhook Endpoints or secrets,
- reading security-relevant Audit Log details,
- exporting Audit Log data,
- changing Payment Expiration,
- changing Late Acceptance Window,
- changing Payment Tolerance,
- changing Confirmation Requirements,
- changing Blockchain Observation Mode or provider configuration,
- importing native ETH Address Pool entries,
- manual Settlement of a Payment,
- manually resending failed Webhook Deliveries,
- risky admin MCP write actions.

Step-up success and denial for security-relevant actions are Audit Log events.

## Recovery

The MVP does not provide email-based self-service password reset.

MFA recovery uses recovery codes:

- generated only for an authenticated Admin Account,
- shown only once,
- stored only as protected hashes,
- single-use,
- revocable,
- audited when generated, used, or revoked.

Operator recovery for complete Admin lockout must be documented before
production use. It must avoid writing raw passwords, MFA secrets, recovery
codes, or session values to logs or Audit Log entries.

## First-Admin Bootstrap

The first local Admin Account is provisioned through the dedicated local
operations command decided by
[ADR 0020](../adr/0020-the-first-admin-is-created-by-a-local-command.md).
The command is an explicit operator action after migrations and is not exposed
through HTTP or MCP.

Bootstrap succeeds only while no Admin Account exists and serializes the
empty-state check with account creation. It reads the password through a
masked interactive prompt, stores only protected password and Recovery Code
hashes, and shows Recovery Codes once.

It asks for no second factor
([ADR 0028](../adr/0028-the-second-factor-is-optional-and-enrolled-later.md)).
Requiring one here meant an operator had to agree a secret between a secret
store, an authenticator app and this command before any account existed to
attach it to, and a setup step that can fail after prompting for a password and
before producing anything is the step people abandon.

Normal API or Web startup never seeds Admin Accounts. Passwords, current TOTP
codes, Recovery Codes, and reusable bootstrap tokens must not be supplied
through command arguments, environment variables, Compose defaults, logs, or
Audit Log entries. A raw TOTP secret for an account that references one may use
the documented non-versioned server-side runtime secret mechanism, but must not
have a versioned value or enter logs or Audit Log entries. After the first
account exists, authenticated Admin management and the separate operator
lockout-recovery procedure are the only account-management paths.

## Generic Security Responses

Security-sensitive flows return generic responses when specific details would
enable enumeration or abuse.

This applies at least to:

- Admin login,
- MFA validation,
- recovery-code use,
- Integration API bearer-token authentication,
- missing or invalid CSRF evidence,
- authorization denial.

Internal reason codes may be recorded in Audit Log entries and safe technical
diagnostics.

## Rate Limits And Lockout

Publicly reachable Admin login, MFA, recovery, Integration API, and remote MCP
endpoints require rate limits or equivalent abuse protection.

Admin authentication mutations use a fixed-window default of 30 requests per
5 minutes for each route and source IP partition. The default applies to
password login start, MFA completion including Recovery Code use, Step-up, and
Recovery Code generation or rotation. Operators can override the default
through `Admin:Authentication:RateLimitPermitLimit` and
`Admin:Authentication:RateLimitWindow`.

Local password login requires account lockout or an equivalent local protection
mechanism. Lockout and rate-limit decisions that indicate abuse or account
protection are security-relevant.

Integration API and remote MCP rate-limit defaults are separate implementation
work.

## Audit Log

Security-relevant Audit Log entries use the shared core schema:

- `event_id`,
- `occurred_at`,
- `event_type`,
- `outcome`,
- `actor_type`,
- `actor_id`,
- `source_service`,
- `source_ip`,
- `user_agent`,
- `correlation_id`,
- `reason_code`,
- `subject_type`,
- `subject_id`.

`occurred_at` is a UTC instant. Audit Log entries are append-only during normal
product operation. Corrections are recorded as new Audit Log entries.

`actor_type` uses shared values where possible:

- `product_user` for local Admin Accounts,
- `system` for internal system actions.

Product-specific subject types include:

- `admin_account`,
- `integration_api_credential`,
- `webhook_endpoint`,
- `payment`,
- `address_pool_import`,
- `configuration`,
- `audit_log`,
- `admin_mcp_action`,
- `admin_session`.

Audit outcomes use:

- `success`,
- `failure`,
- `denied`,
- `expired`,
- `revoked`.

Audit Log entries must not contain raw secrets, tokens, passwords, MFA secrets,
recovery codes, CSRF tokens, session identifiers, provider raw payloads,
stacktraces, or SQL details.

Security-relevant Audit Log entries are retained for at least 180 days.

## Required Audit Events

Audit Log entries are required for:

- Admin login success and failure,
- Admin logout or session ended,
- Admin Account created, disabled, or changed,
- password changed or reset through an operator-controlled process,
- MFA factor enabled, disabled, replaced, or reset,
- recovery codes generated, used, or revoked,
- step-up success or denial for security-relevant actions,
- manual Settlement of a Payment,
- creating or disabling Integration API Credentials,
- creating, disabling, changing, or rotating Webhook Endpoints or secrets,
- importing native ETH Address Pool entries,
- changing Payment Expiration,
- changing Late Acceptance Window,
- changing Payment Tolerance,
- changing Confirmation Requirements,
- changing Blockchain Observation Mode or provider configuration,
- accessing security-relevant Audit Log details,
- exporting Audit Log data,
- authorization denial for administrative or especially sensitive actions,
- account lockout or security-relevant rate-limit decisions,
- risky admin MCP write actions allowed or denied.

Payment lifecycle changes are recorded in Payment Event History. Webhook
Delivery attempts and failures are recorded in Webhook Delivery history. They
are Audit Log entries only when the action is security-relevant, such as manual
resend by an Admin.

## Audit Access And Export

Because the MVP has one Admin permission level, Audit Log access is an explicit
capability of every Admin Account once signed in, and of an account with a
second factor after step-up. This is a
product-local simplification. A future role model may split Audit access into a
separate permission.

Audit exports are security-relevant. An Audit export records at least:

- Admin Account actor,
- timestamp,
- export scope,
- filters,
- affected product or installation,
- result.

Exports must apply the same data minimization rules as Audit Log reads.

## Tests

Implementation must include focused tests for:

- first-Admin bootstrap succeeding exactly once under concurrent attempts,
- bootstrap refusing when an Admin Account already exists,
- bootstrap refusing invalid or unavailable TOTP configuration without
  creating an account,
- bootstrap output and diagnostics excluding password, TOTP, Recovery Code
  hashes, and connection-string values,
- successful and failed Admin login,
- Admin access with an optional second factor,
- session absolute and inactivity limits,
- logout ending the local Admin session,
- unsafe Admin browser requests without CSRF evidence being rejected,
- unsafe Admin browser requests with invalid CSRF evidence being rejected,
- unsafe Admin browser requests with valid CSRF evidence being accepted,
- server-side authorization for protected Admin actions,
- fail-closed behavior when actor, session, MFA, step-up, or policy context is
  missing,
- generic external security responses,
- required Audit Log entries without sensitive raw values,
- recovery-code single-use behavior,
- rate-limit or lockout behavior once concrete limits are configured.
