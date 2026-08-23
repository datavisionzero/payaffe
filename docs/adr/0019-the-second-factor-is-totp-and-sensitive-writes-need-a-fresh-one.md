# The Second Factor Is TOTP, and Sensitive Writes Need a Fresh One

**Amended by [ADR 0028](./0028-the-second-factor-is-optional-and-enrolled-later.md).**
A second factor is optional. What follows describes it for an account that has
one, which is still exactly how it behaves.

Signing in is a password and a TOTP code. Both are required; there is no
password-only admin. WebAuthn is the stronger factor and was not chosen, because
TOTP needs nothing from the operator's environment — no domain that resolves the
way the authenticator expects, no hardware to buy, no key to lose without a
recovery story — and the operator here is one person installing a compose file.

The second half is the part that does real work. A session lasts up to seven
days with a twelve-hour inactivity limit, which is convenient and means an
open laptop is an authenticated laptop. So a sensitive action requires that the
MFA-backed authentication be no older than fifteen minutes, and asks again when
it is not: credential management, webhook endpoint secrets, audit log access and
export, configuration and observation mode changes, manual settlement, ETH pool
imports, and risky MCP writes. A long session for reading, a short one for
anything that moves money or changes who can.

Session cookies are `HttpOnly`, `Secure`, `SameSite=Lax`. Unsafe
cookie-authenticated requests carry an antiforgery token in `X-CSRF-TOKEN`;
the Integration API does not, because it is bearer-authenticated and therefore
not reachable by a browser acting on someone's behalf
([ADR 0012](./0012-the-integration-api-takes-a-static-token-not-oauth.md)).

## Consequences

**Losing the authenticator has an answer, and it is recovery codes.** They are
shown once, stored as hashes, single-use, and audited on creation, use, and
revocation. There is no email reset, because there is no mail: adding SMTP to a
product whose operational story is a compose file would be a larger decision
than the one it solves.

**Remembered devices do not exist.** They are the standard way to make step-up
tolerable and they would undo it, since the device that is remembered is the
laptop that is open.

**Fifteen minutes is a number that will feel wrong at least once.** It is short
enough to matter and long enough that a working session does not become a
sequence of prompts. It is configuration-adjacent rather than configuration on
purpose — moving it should be a decision, not a tuning.

**Every sensitive route must go through one authorization helper.** Two
mechanisms enforcing the same rule is how a new route quietly gets only half of
it, and a test that enumerates the protected routes is what keeps that from
happening.
