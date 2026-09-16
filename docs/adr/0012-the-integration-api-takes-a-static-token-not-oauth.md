# The Integration API Takes a Static Token, Not OAuth

An external system authenticates with a bearer token that does not expire and
was created by an admin. There is no authorization server, no client credentials
flow, no JWT validation, and no key distribution.

The number that decides this is the number of integrations: one installation
serves one shop ([ADR 0017](./0017-one-installation-serves-one-shop.md)), and a
shop has a handful of systems talking to it, all of which the same operator
configured. OAuth solves delegation, third-party clients, and scoped consent —
none of which exist here. What it would add is an authorization server to run in
a product whose deployment budget is counted in compose services
([ADR 0001](./0001-deployment-is-compose-and-the-service-count-is-the-budget.md)).

One global API key was rejected, and that is the part worth recording. Multiple
named credentials cost almost nothing and buy the two things that matter when
something goes wrong: revoking one integration without breaking the others, and
being able to tell from the audit log which one did something.

## Consequences

**The token is shown once and stored as a hash.** A credential that is lost is
rotated, not recovered. Each credential carries a name, a status, a created
timestamp, and a last-used timestamp — the last of which is what tells an
operator whether a credential is still in use before they revoke it.

**Webhook endpoints hang off credentials.** An endpoint belongs to a credential
and has its own URL, secret, status, and event selection, so disabling an
integration disables both directions of it at once.

**There is no expiry, and therefore rotation is a documented act.** Nothing
forces a token to be replaced, so the procedure for replacing one — including
the overlap while both are valid — is written down in
[credential-rotation.md](../operations/credential-rotation.md) rather than left
to be improvised.

**Bearer auth means no CSRF concern on this surface**, which is why the
Integration API is exempt from the antiforgery handling the cookie-authenticated
admin surface requires.

**The credential is backend-only.** An embedded payment screen calls its own
product backend, which authenticates its customer and invokes Payaffe. A static
bearer token is never a browser, mobile, or desktop application credential;
ADR 0031 records that trust boundary for the embedded flow.

If this product ever serves integrations the operator did not configure
themselves, this is the decision to reopen first.
