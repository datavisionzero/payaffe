# One Installation Serves One Shop

Superseded by
[ADR 0029](./0029-one-operator-can-isolate-payment-projects.md). The
single-operator boundary remains; the decision that all resources and settings
are installation-wide does not.

There is no tenant. Not a tenant column with one value in it, not a default
tenant, not a tenant resolved from a header — the concept is absent, and a shop
that wants its own `payaffe` runs its own `payaffe`.

Multi-tenancy is the kind of thing that looks like a column and is actually a
property of every query, every index, every authorization check, every audit
entry, every configuration value, and every support conversation. Getting it
wrong once means one shop can see another's payments, which for this product is
the worst outcome available. Adding it later is a redesign, and this ADR is
honest about that rather than pretending the door is open.

What makes the trade acceptable is that the deployment is a compose file
([ADR 0001](./0001-deployment-is-compose-and-the-service-count-is-the-budget.md)).
The cost of a second shop is a second installation, and that cost is low
precisely because the product refused to become a platform.

## Consequences

**Several admins, one shop.** Multiple Admin Accounts exist and all of them
administer the same single installation; they are colleagues, not tenants.

**Configuration is installation-wide.** The observation mode, the confirmation
requirements, the tolerance, the expiration — all of it is one set of values,
which is why they can be plain environment variables rather than rows.

**Integration credentials do not isolate anything.** A credential scopes
idempotency and identifies a caller in the audit log
([ADR 0012](./0012-the-integration-api-takes-a-static-token-not-oauth.md)); it
does not partition data, and it must never be mistaken for a boundary that
would let one credential hide payments from another.

**A hosted multi-shop offering is a different product.** If that is ever wanted,
this is the decision to reopen, and reopening it means redesigning the data
model rather than adding a filter.
