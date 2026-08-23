# Security Policy

payaffe takes cryptocurrency payments on behalf of one shop. Two of its surfaces
are meant to be reachable from the public internet — the payer page and the
Integration API — and a third, the Admin UI, is reachable by whoever can reach
the host. Security is treated as part of the product rather than as something
the operator is expected to solve with a reverse proxy in front of it.

One property shapes everything below: **payaffe is non-custodial and holds no
key that can spend**
([ADR 0005](docs/adr/0005-payaffe-never-holds-a-key-that-can-spend.md)). An
attacker who takes the database gets payment history, receiving addresses,
hashed admin credentials, and hashed integration tokens. They do not get funds,
because the funds were never reachable from here. Reports are prioritised on
that basis: the worst outcomes available are a payment wrongly reported as paid,
and disclosure.

## Reporting a vulnerability

Please report security issues privately through GitHub's private vulnerability
reporting: open the **Security** tab of `datavisionzero/payaffe` and choose
**Report a vulnerability**.

Do not open a public issue for a suspected vulnerability, and do not disclose it
publicly before a fix is available.

Please include:

- what the issue is and which surface it affects (payer page, Integration API,
  Admin UI, admin MCP, worker, container image, Compose setup),
- the steps needed to reproduce it,
- the version or commit you tested against,
- the impact you believe it has.

## What to expect

This is a small project maintained by a single person. Reports are acknowledged
as soon as they are seen, and fixes are prioritized over other work. There is no
bug bounty.

## Supported versions

payaffe is pre-release. Only the current `main` branch receives security fixes;
there are no maintained release branches yet. This section will be updated once
versioned releases exist.

## Scope

In scope:

- anything that makes a payment appear completed when it was not, or attributes
  a payment to the wrong external reference,
- Integration API authentication and idempotency, including a credential
  reaching a payment it did not create,
- outgoing webhook signing, and anything that lets a receiver be convinced of an
  event payaffe did not send,
- Admin authentication, TOTP, recovery codes, session handling, CSRF, and the
  step-up requirement on sensitive writes
  ([ADR 0019](docs/adr/0019-the-second-factor-is-totp-and-sensitive-writes-need-a-fresh-one.md)),
- the admin MCP surface, including anything that widens it beyond its declared
  tool list, which is a security boundary
  ([ADR 0023](docs/adr/0023-mcp-is-narrower-than-the-admin-ui-and-stays-local.md)),
- disclosure of secrets — integration tokens, webhook endpoint secrets, provider
  API keys, extended public keys, TOTP secrets, recovery codes — through any
  response, log, audit entry, or contract snapshot,
- native ETH Address Pool handling, in particular an assigned address becoming
  available again,
- the default configuration of the published container images and the Compose
  setup.

Out of scope:

- **a hosted blockchain provider reporting a transaction that did not happen.**
  payaffe accepts a third-party API as Blockchain Truth by design and documents
  that it does
  ([ADR 0003](docs/adr/0003-blockchain-truth-comes-from-a-hosted-api-not-a-node-we-run.md)).
  Confirmation requirements are the documented mitigation. A flaw in how payaffe
  *validates* a provider response is in scope; the provider being wrong is not,
- **a completed payment not being reversed after a chain reorganisation.**
  Completed payments stay final for the external system and a reorg raises an
  admin-visible alert instead. This is a deliberate product decision,
- weaknesses that require the attacker to already have host access to the
  machine payaffe runs on. The first-admin bootstrap command is a documented and
  intentional operator escape hatch
  ([ADR 0020](docs/adr/0020-the-first-admin-is-created-by-a-local-command.md)),
- an admin performing an action another admin should not have permitted. There
  are no roles, and every Admin Account is privileged by design
  ([ADR 0018](docs/adr/0018-every-admin-is-fully-privileged.md)),
- rate-limit exhaustion by an integration holding a valid credential. Those are
  issued by the operator to their own systems, which are trusted by design,
- free-tier hosted API throttling causing missed or delayed detection. This is a
  documented operational risk of running on a free provider plan.

## No warranty

payaffe is provided under the MIT License, without warranty of any kind. See
[LICENSE](LICENSE).
