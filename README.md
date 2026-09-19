# payaffe

A self-hostable, non-custodial way to take cryptocurrency payments for one shop.
An external system creates a payment in euros or dollars, the payer picks BTC,
LTC, or native ETH and pays it, and the shop is told when the money arrived —
over a webhook, or by asking.

payaffe holds no key that can spend. It watches addresses the operator owns and
reports what arrived; it cannot sweep, refund, or withdraw, and no admin action
can make it. That is the decision the rest of the product is shaped around
([ADR 0005](docs/adr/0005-payaffe-never-holds-a-key-that-can-spend.md)).

See [VISION.md](VISION.md) for what payaffe is and, just as importantly, what it
deliberately is not.

## Status

**Released.** Images are published to `ghcr.io/datavisionzero` for `linux/amd64`
and `linux/arm64`, and an installation is the two files below. The `0.x`
is deliberate: the Integration API and the webhook contract are versioned and
snapshotted, but they have not yet been depended on by anyone, and `1.0.0` is
reserved for the point where breaking them would cost something. Pin
`PAYAFFE_VERSION` to a version rather than leaving it at `latest`, and read the
release notes before moving it. A `main` tag is published too and is not for
installations — it is the trunk, and it exists so a staging environment can
follow it; see
[docs/operations/docker-compose.md](docs/operations/docker-compose.md).

What works end to end: payment creation over the Integration API with
idempotency, the payer page with currency selection and rate locking, address
assignment from a watch-only wallet source or the ETH pool, blockchain
observation against Blockchair or NOWNodes, confirmation and completion, late
and partial payment handling, signed webhook delivery with retries, the Admin UI
behind TOTP with step-up on sensitive writes, an admin MCP surface for an agent
CLI, logs delivered to a [logaffe](https://github.com/datavisionzero/logaffe)
installation, and OpenTelemetry metrics with shipped Grafana and Prometheus
assets. Payment creation, currency selection, payment instructions, and
reconciliation are also drivable entirely through the Integration API and the
published .NET SDK, for a product that embeds the flow in its own checkout.

Known gaps are tracked as goals in the repository rather than as issues.

## Who it is for

An operator running one shop, taking roughly five to twenty payments a day of
ten to twenty euros each, who wants to accept cryptocurrency without running
blockchain infrastructure or handing custody to a payment processor.

It is explicitly **not** a merchant platform: one installation serves one shop
([ADR 0017](docs/adr/0017-one-installation-serves-one-shop.md)), and there is no
tenant concept to add one to.

## What you need

- Docker and Docker Compose.
- A receiving wallet you control — an extended public key for BTC and LTC, and a
  list of Ethereum addresses for native ETH.
- An API key for a hosted blockchain provider (Blockchair or NOWNodes) if you
  want payments to be detected. The default observation mode is `none`.
- Optionally a CoinGecko key, for exchange rates under load.

payaffe accepts a hosted API as the authority on whether a payment arrived,
rather than running full nodes. That trade is deliberate and is written up in
[ADR 0003](docs/adr/0003-blockchain-truth-comes-from-a-hosted-api-not-a-node-we-run.md).

## Running it

An installation is two files and a volume. Neither file needs a checkout of
this repository — the images come from `ghcr.io/datavisionzero`:

```sh
mkdir payaffe && cd payaffe
base=https://raw.githubusercontent.com/datavisionzero/payaffe/main/deploy
curl -O    "$base/compose.yaml"
curl -o .env "$base/.env.example"
chmod 600 .env
# edit .env: database password, public addresses, provider key, wallet sources
docker compose up -d
```

The hosts bring the schema up to date as they start
([ADR 0027](docs/adr/0027-migrations-apply-on-startup.md)), so there is no
migration step to run and no order to remember. Upgrading is `pull` and `up`.
Back up the database before an upgrade: there is no downgrade, so that artifact
is the rollback.

Put a reverse proxy in front of it to terminate TLS. The API host serves the
built web application, `/api/`, and `/health/` from one address, and its
published port binds to `127.0.0.1`. A complete Caddy configuration is in
[docs/operations/docker-compose.md](docs/operations/docker-compose.md).

Then create the first admin. There is no first-run registration page, on purpose
([ADR 0020](docs/adr/0020-the-first-admin-is-created-by-a-local-command.md)) —
one command, which prompts for a password:

```sh
docker compose --profile operations run --rm migrations \
  bootstrap-admin --username admin@example.test
```

The account signs in with that password. Adding a second factor is the admin's
own, later ([ADR 0028](docs/adr/0028-the-second-factor-is-optional-and-enrolled-later.md)).

It prints recovery codes exactly once. The full procedure, including what must
never reach your shell history, is in
[docs/operations/docker-compose.md](docs/operations/docker-compose.md).

The payer page is at `/pay/{id}` and the Admin UI at `/admin`. Full setup,
backup, and rotation procedures are in [docs/operations/](docs/operations/).

## Embedding it in your own checkout

A product that would rather keep its payer inside its own checkout does not have
to send them to the payer page. Its backend creates the payment, submits the
currency the payer picked, and renders the returned amount, address, and URI in
its own UI. payaffe never sees that browser
([ADR 0031](docs/adr/0031-embedded-payments-use-the-integration-api.md)).

The supported client for that is
[`Payaffe.Sdk`](https://www.nuget.org/packages/Payaffe.Sdk), a UI-free `net10.0`
package on nuget.org:

```sh
dotnet add package Payaffe.Sdk
```

It speaks `/api/v1` and carries its own version, so upgrading an installation
does not oblige an integrator to take a new package and a fix in the client does
not claim a server release that never happened. It keeps the bearer token in the
backend, where it has to stay: a browser-to-payaffe integration is out of scope
precisely because that token cannot be handed to a browser.

The package README, in [src/Payaffe.Sdk/](src/Payaffe.Sdk/README.md), is the
integration guide: the trust boundary, the flow, the retry rules, polling, and
how to verify a webhook before believing it.

## Development

```sh
dotnet test Payaffe.slnx          # 400 tests; integration tests need Docker
pnpm install
pnpm web:check                   # generate, test, typecheck, e2e, build
```

## Documentation

- [docs/](docs/) — the map.
- [CONTEXT.md](CONTEXT.md) — the domain language. Terms here are used precisely;
  `Payment`, `Observed Payment`, and `Settlement` all mean specific things.
- [docs/adr/](docs/adr/) — why the obvious alternative was not chosen.
- [docs/architecture/](docs/architecture/) — what the implementation must do.
- [docs/product/requirements.md](docs/product/requirements.md) — the product
  rules, including the non-goals.

## Security

payaffe handles money. Please read [SECURITY.md](SECURITY.md) before reporting
an issue — it states what is in scope, and names two behaviours that look like
vulnerabilities and are documented decisions.

## License

[MIT](LICENSE).
