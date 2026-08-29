# payaffe — Product Vision

## In one sentence

payaffe is a self-hostable, non-custodial way for one shop to accept
cryptocurrency payments: an external system creates a payment in euros or
dollars, the payer sends BTC, LTC, or native ETH, and the shop is told when the
money arrived — without anyone running a blockchain node or holding a key that
can spend.

## Problem

A small shop that wants to accept cryptocurrency has two realistic options
today, and both are bad for it.

The first is a hosted payment processor. It works, and it means the payments run
through someone else's company: custody of the funds, KYC on the shop, a cut of
each transaction, and a relationship that can be terminated. For a shop taking
fifteen payments a day at fifteen euros, the terms are set by a counterparty who
does not particularly want the business.

The second is running the infrastructure. Full nodes for three chains, each with
its storage, bandwidth, initial sync, monitoring, and upgrade duty. The
trustlessness is real and the operational burden is larger than the payment
volume justifies.

payaffe takes a third position, and is explicit about what it costs: it does not
hold funds and it does not run nodes. It watches addresses the operator already
controls, and it asks a hosted blockchain API whether money arrived.

## Target user and scenario

One operator, running one shop, taking roughly five to twenty payments a day of
ten to twenty euros each. They can run a compose file and they already have a
wallet they control. They want the payment to land in their own wallet, and they
want their shop software to be told about it.

They are not a payment platform, they do not serve other merchants, and they do
not have an operations team. Every decision in this product is sized for that
person.

## Trust boundaries

**payaffe never holds a key that can spend.** No private key, no seed phrase,
nothing in the database that could move a coin. It cannot sweep, refund, or
withdraw, and no admin action and no agent can make it. Someone who steals the
database gets payment history, receiving addresses, and hashed credentials —
not funds.

**A hosted API decides whether a payment arrived.** This is the deliberate
trade, and stating it plainly is part of the product. The provider could in
principle report a transaction that did not happen, and the installation would
believe it. Confirmation requirements per currency are the mitigation, and the
operator chooses the provider.

**The addresses are the operator's.** BTC and LTC are derived from an extended
public key they configure; native ETH addresses are imported from a wallet they
already have. payaffe receives only public material.

**A completed payment stays completed.** A chain reorganisation raises an alert
for the admin rather than retracting a payment the shop has already shipped
against.

## What it does

An integration creates a payment with a fiat amount, an external reference, and
an idempotency key. The payer opens a page hosted by the installation, picks a
currency, and gets an address and an amount fixed at that moment. Background
workers watch for the transaction, count confirmations, and complete the
payment. The shop learns about it through a signed webhook, or by polling the
same API it created the payment with.

An admin signs in with a password, and with a second factor once they have
enrolled one, to see payments, resolve the cases automation should not decide
alone, manage integration credentials and webhook endpoints, and read the audit
log. An agent CLI reaches a deliberately narrower version of that over MCP.

The detailed product rules — statuses, tolerances, late payments, expiration,
evidence storage, and the full admin scope — are
[docs/product/requirements.md](docs/product/requirements.md).

## Non-goals

These are not backlog items. They are the shape of the product.

- **Custody, and therefore refunds.** A refund requires spending, and spending
  is the thing that is absent.
- **Multi-tenancy.** One installation serves one shop. There is no tenant
  concept to configure, and a second shop is a second installation.
- **Full-node verification.** Not required, not offered as the default.
- **High throughput.** Sized for tens of payments a day, not hundreds a minute.
- **Token payments.** ERC-20 and anything like it are out; native ETH only.
- **Monero.**
- **Splitting one payment across currencies.** The payer picks one.
- **External identity providers.** No OIDC, SAML, or LDAP for admin sign-in.
- **OAuth for integrations.** A static bearer token the operator issued to their
  own systems.
- **Admin roles.** Every admin is fully privileged.

## Technical direction

What was chosen, without the argument. The reasoning is in
[docs/adr/](docs/adr/) — an entry here names what was chosen, an ADR there
explains why the obvious alternative was not.

- .NET 10 for the backend, with four hosts over a shared core: `api`, `worker`,
  `mcp`, `migrations`.
- PostgreSQL as the only datastore. Jobs, the outbox, and the scheduler are
  tables, not a broker.
- Next.js App Router with React and TypeScript for the payer page and the Admin
  UI, in one application.
- Docker Compose for deployment, with the service count treated as a budget.
- Blockchair or NOWNodes for blockchain observation, one selected at a time.
- CoinGecko for exchange rates, with a cache and a bounded stale window.
- logaffe for logs, errors included; OpenTelemetry over OTLP for traces and
  metrics.
- MIT licensed, developed in the open on GitHub.

## Operating an installation

An installation is a compose file, a `.env`, and a PostgreSQL volume. The hosts
bring the schema up to date as they start, so an upgrade is a pull and an up
with no step in between. The database is the backup target, and a restore has to
keep assigned ETH addresses assigned.

Setup, backup, rotation, and incident procedures are in
[docs/operations/](docs/operations/).

## Guiding principles

**State the trade, do not imply the guarantee.** The product trusts a hosted API
and says so, in the README, in the security policy, and in the ADR that decided
it. A reader should never discover a trust assumption by reading the code.

**The service count is a budget.** Every long-running process is one more thing
the operator has to keep alive. PostgreSQL is tried before anything new is
added.

**Explainable over clever.** Payment detection, settlement, webhook delivery,
and audit behaviour have to be explainable to someone reconciling a payment
that went wrong. That is worth more here than throughput.

**The non-custodial boundary is not negotiable by accident.** Moving it takes an
ADR that argues for custody, not a field added to a wallet source.
