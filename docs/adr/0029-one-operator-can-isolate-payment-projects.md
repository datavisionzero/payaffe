# One Operator Can Isolate Payment Projects

Supersedes [ADR 0017](./0017-one-installation-serves-one-shop.md).

One `payaffe` installation still serves one operator or shop, but that operator
may create multiple Projects. A Project is the ownership boundary for payment
data, Integration API Credentials, receiving-address allocation, Webhook
Delivery, and payment-policy configuration; it is not a tenant or an Admin
authorization boundary. All Admins can administer every Project, as decided by
[ADR 0018](./0018-every-admin-is-fully-privileged.md).

The former one-shop decision made every resource and setting installation-wide.
That is too coarse for an operator embedding payments into several products:
credentials, callbacks, address supply, external references, and operational
history can then be mixed accidentally. A tenant model with project membership
and roles was rejected because the operator does not need to delegate Projects
to mutually distrusting users. Separate deployments were rejected because they
duplicate the database, workers, provider configuration, and operations for one
operator.

## Consequences

**Isolation uses shared tables with explicit Project ownership.** Every
Project-owned row carries `project_id`; relationships use Project-preserving
foreign keys, and Project-scoped application stores fail closed without an
explicit Project context. Separate databases or schemas per Project and
PostgreSQL row-level security are not used. They add deployment and connection
complexity without creating a useful Admin security boundary, while composite
constraints and scoped stores protect the credential-facing boundary directly.

**A credential selects exactly one Project.** Integration API Credentials are
Project-owned. Within that Project, a credential may inspect all Payments;
credentials remain caller identities and idempotency scopes rather than a
second data partition.

**Some infrastructure remains shared.** Blockchain Observation Mode and
provider credentials, the Exchange Rate Source and Rate Cache, Admin Accounts,
worker policy, and observability remain installation-wide. Payment risk and
address-supply settings are Project-owned. A Payment snapshots the applicable
policy before later configuration changes can affect it.

**Address uniqueness is installation-wide.** Projects may bind the same BTC or
LTC Watch-Only Wallet Source, but all bindings share one atomic derivation
cursor for the same address space. Native ETH addresses and every assigned
Payment Address are also unique across the installation. Project isolation
must never permit address reuse.

The detailed ownership, lifecycle, migration, and threat rules are in
[project-isolation-baseline.md](../architecture/project-isolation-baseline.md).
