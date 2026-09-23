# Architecture Decisions

This directory contains decisions that are difficult to reverse, would be
surprising without context, and resulted from a genuine trade-off. Product scope
and promises belong in the [product vision](../../VISION.md) and
[requirements](../product/requirements.md) instead; the technical roster —
what was chosen, without the argument — is
[technical-constraints.md](../architecture/technical-constraints.md). An entry
there names what was chosen, an ADR here explains why the obvious alternative
was not.

The detailed rules that follow from these decisions live in the baseline
documents under [architecture/](../architecture/). An ADR states the decision
and its cost; the baseline states what the implementation must do.

## Naming

ADRs are numbered sequentially as `NNNN-short-slug.md`. The next number follows
the highest existing number.

## Short form

```md
# Short decision title

One to three sentences describe the context, decision, and rationale.
```

Status, considered options, and consequences are included only when they add
material value to understanding the decision.

## Decisions

- [0001 – Deployment is Compose, and the service count is the budget](./0001-deployment-is-compose-and-the-service-count-is-the-budget.md) — service roster amended by 0030
- [0002 – The hosts are thin and the core is shared](./0002-the-hosts-are-thin-and-the-core-is-shared.md) — frontend topology amended by 0030
- [0003 – Blockchain truth comes from a hosted API, not a node we run](./0003-blockchain-truth-comes-from-a-hosted-api-not-a-node-we-run.md) — amended for Test Mode by 0033
- [0004 – One observation provider is selected, and never mixed](./0004-one-observation-provider-is-selected-and-never-mixed.md) — amended for Test Mode by 0033
- [0005 – payaffe never holds a key that can spend](./0005-payaffe-never-holds-a-key-that-can-spend.md)
- [0006 – Native ETH addresses are imported, not derived](./0006-native-eth-addresses-are-imported-not-derived.md)
- [0007 – A payment is created in fiat, and payaffe converts](./0007-a-payment-is-created-in-fiat-and-payaffe-converts.md)
- [0008 – The rate locks when the payer picks a currency](./0008-the-rate-locks-when-the-payer-picks-a-currency.md)
- [0009 – Integrations get webhooks and polling, because either alone fails](./0009-integrations-get-webhooks-and-polling-because-either-alone-fails.md)
- [0010 – A webhook is signed, versioned, and at least once](./0010-a-webhook-is-signed-versioned-and-at-least-once.md)
- [0011 – The Integration API versions in the path](./0011-the-integration-api-versions-in-the-path.md) — v1 surface extended by 0031
- [0012 – The Integration API takes a static token, not OAuth](./0012-the-integration-api-takes-a-static-token-not-oauth.md)
- [0013 – A payment change and the events it causes share one transaction](./0013-a-payment-change-and-the-events-it-causes-share-one-transaction.md)
- [0014 – Durable work runs on PostgreSQL, not on a queue](./0014-durable-work-runs-on-postgresql-not-on-a-queue.md)
- [0015 – The schema is split by what it would cost to leak](./0015-the-schema-is-split-by-what-it-would-cost-to-leak.md)
- [0016 – Migrations are a step, not a startup side effect](./0016-migrations-are-a-step-not-a-startup-side-effect.md) — superseded by 0027
- [0017 – One installation serves one shop](./0017-one-installation-serves-one-shop.md) — superseded by 0029
- [0018 – Every admin is fully privileged](./0018-every-admin-is-fully-privileged.md) — clarified by 0029
- [0019 – The second factor is TOTP, and sensitive writes need a fresh one](./0019-the-second-factor-is-totp-and-sensitive-writes-need-a-fresh-one.md) — amended by 0028
- [0020 – The first admin is created by a local command](./0020-the-first-admin-is-created-by-a-local-command.md)
- [0021 – Secrets live in the environment, never in the repository](./0021-secrets-live-in-the-environment-never-in-the-repository.md)
- [0022 – The backend owns every protected mutation](./0022-the-backend-owns-every-protected-mutation.md) — frontend topology amended by 0030
- [0023 – MCP is narrower than the Admin UI, and stays local](./0023-mcp-is-narrower-than-the-admin-ui-and-stays-local.md)
- [0024 – The browser API lives under /api, on one origin](./0024-the-browser-api-lives-under-api-on-one-origin.md) — serving topology amended by 0030
- [0025 – Logs are delivered to logaffe, and only logs](./0025-logs-are-delivered-to-logaffe.md)
- [0026 – An error is an entry, and there is no error tracker](./0026-an-error-is-an-entry-and-there-is-no-error-tracker.md)
- [0027 – Migrations apply on startup](./0027-migrations-apply-on-startup.md)
- [0028 – The second factor is optional, and enrolled later](./0028-the-second-factor-is-optional-and-enrolled-later.md)
- [0029 – One operator can isolate payment Projects](./0029-one-operator-can-isolate-payment-projects.md)
- [0030 – The web application is a Vite SPA served by the API](./0030-the-web-application-is-a-vite-spa-served-by-the-api.md)
- [0031 – Embedded payments use the Integration API](./0031-embedded-payments-use-the-integration-api.md)
- [0032 – The client address is the connection, until a proxy is named](./0032-the-client-address-is-the-connection-until-a-proxy-is-named.md)
- [0033 – A test installation simulates its external truth](./0033-a-test-installation-simulates-its-external-truth.md)
