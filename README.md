# payaffe

A self-hostable, non-custodial way to take cryptocurrency payments for one shop.
An external system creates a payment in euros or dollars, the payer picks BTC,
LTC, or native ETH and pays it, and the shop is told when the money arrived —
over a webhook, or by asking.

payaffe holds no key that can spend. It watches addresses the operator owns and
reports what arrived; it cannot sweep, refund, or withdraw, and no admin action
can make it. That is the decision the rest of the product is shaped around
([ADR 0005](docs/adr/0005-payaffe-never-holds-a-key-that-can-spend.md)).

See [docs/product/vision.md](docs/product/vision.md) for what payaffe is, and
[the non-goals](docs/product/requirements.md#non-goals-for-the-mvp) for what it
deliberately is not.

## Status

**Pre-release.** The product is built and version-stamped `1.0.0-rc.1`, but
there has not been a release, there are no tags yet, and no container images are
published. Everything below runs from a checkout.

What works end to end: payment creation over the Integration API with
idempotency, the payer page with currency selection and rate locking, address
assignment from a watch-only wallet source or the ETH pool, blockchain
observation against Blockchair or NOWNodes, confirmation and completion, late
and partial payment handling, signed webhook delivery with retries, the Admin UI
behind TOTP with step-up on sensitive writes, an admin MCP surface for an agent
CLI, and OpenTelemetry metrics with shipped Grafana and Prometheus assets.

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

```sh
cp .env.example .env
# edit .env: database password, provider keys, wallet sources
docker compose --profile operations run --rm migrations
docker compose up -d
```

Migrations are a deliberate step and never a startup side effect
([ADR 0016](docs/adr/0016-migrations-are-a-step-not-a-startup-side-effect.md)),
which is why they run first and under their own profile.

Then create the first admin. There is no first-run registration page, on purpose
([ADR 0020](docs/adr/0020-the-first-admin-is-created-by-a-local-command.md)) —
you generate a TOTP secret, put it in `.env`, and run an interactive command
that prompts for the password and a current code:

```sh
openssl rand 20 | base32 | tr -d '\n'   # into PAYAFFE_ADMIN_TOTP_SECRET_FIRST_ADMIN

docker compose --profile operations run --rm migrations \
  bootstrap-admin \
  --username admin@example.test \
  --totp-secret-reference configuration:Admin:TotpSecrets:first-admin
```

It prints recovery codes exactly once. The full procedure, including what must
never reach your shell history, is in
[docs/operations/docker-compose.md](docs/operations/docker-compose.md).

The payer page is at `/pay/{id}` and the Admin UI at `/admin`. Full setup,
backup, and rotation procedures are in [docs/operations/](docs/operations/).

## Development

```sh
dotnet test Payaffe.sln          # 279 tests; integration tests need Docker
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
