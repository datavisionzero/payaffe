# Docker Compose Operations

This document describes the Docker Compose baseline for `payaffe`.

## The two Compose files

There are two, and picking the wrong one is the first mistake to avoid.

[deploy/compose.yaml](../../deploy/compose.yaml) is how an installation is run.
It pulls published images from `ghcr.io/datavisionzero`, needs no checkout of
this repository, publishes its ports on the loopback interface for a reverse
proxy to sit in front of, and does not expose PostgreSQL at all. Its
[.env.example](../../deploy/.env.example) is the shorter one: it asks for what
an installation must decide and leaves the rest at its default.

The root [compose.yaml](../../compose.yaml) is how payaffe is developed. It
builds every image from the working tree, publishes PostgreSQL so a local tool
can reach it, and defaults its addresses to `localhost`. It is not a deployment
baseline and is not hardened as one.

Both define the same services, and everything below applies to either unless it
says otherwise.

### One origin, and a proxy in front

The API container serves the built Payer/Admin SPA, browser APIs, Integration
API, and health endpoints from one address. A reverse proxy therefore forwards
every path to one upstream:

Caddy, as a whole configuration:

```caddyfile
pay.example.com {
    reverse_proxy api:8080
}
```

nginx uses the same one upstream, with `proxy_set_header Host $host` and
`X-Forwarded-Proto $scheme` so that the API sees the public scheme.

### Telling the API who the caller is

A proxy in front means every request arrives from the proxy, and the API
believes the connection until it is told which hops to believe instead
([ADR 0032](../adr/0032-the-client-address-is-the-connection-until-a-proxy-is-named.md)).
Left unset, the admin login limit, the browser error limit, and the source
address in the Audit Log are the proxy's address and the proxy's single bucket
for every caller in the world.

`PAYAFFE_TRUSTED_PROXIES` is that list: addresses or CIDR ranges, comma
separated. The value is the address the proxy *connects from*, not the one it
listens on. A proxy running in the same Compose project reaches the API over
the bridge network:

```console
$ docker network inspect payaffe_default -f '{{(index .IPAM.Config 0).Subnet}}'
172.30.0.0/24
```

```dotenv
PAYAFFE_TRUSTED_PROXIES=172.30.0.0/24
```

A proxy on the host, reaching the published loopback port, connects from the
bridge gateway instead — the same command's `.Gateway`, typically a single
address. A CDN in front of the proxy is another hop and belongs in the same
list; the forwarded chain is read from the connection outwards and ends at the
first address that was not named. Do not list a range wide enough to contain
real callers: an address in the list may name itself.

Caddy and nginx both send `X-Forwarded-For` with no extra configuration. The
host says which of the two modes it is in with one line at startup, so
`docker compose logs api | grep "Client addresses"` answers whether the setting
took. An entry that is not an address or a CIDR range stops the host with that
entry quoted, rather than starting with nothing trusted.

The browser bundle always calls relative `/api` paths, so the published API
image works at any installation address without a compiled-in URL. `/api` and
`/health` never receive the SPA fallback; eligible Admin and Payer document
navigations do, including direct deep-link reloads.

### Upgrading from the separate web runtime

The Vite migration folds the browser application into the `api` image. An
installation upgrading from a version with a separate Next.js `web` service
must remove that service, its published port, and any reverse-proxy route that
sent browser traffic to it. The proxy sends every path to `api:8080`; no Node.js
process runs in production after the upgrade.

There is no frontend data migration. Existing `/pay/{payerPageId}` links keep
their path and load through the API host's SPA fallback. Admin deep links also
remain reloadable; legacy unscoped Admin paths redirect only when the
installation has one unambiguous Project. The only retained browser values are
non-sensitive UI preferences such as the color theme and last Project ID, and
they may safely be cleared.

After the normal database backup, `pull`, and `up`, verify `/health/ready`, load
one existing Payer link directly, reload one Project-scoped Admin URL, and
confirm an unknown `/api` path and a missing asset still return `404`. A stale
HTML shell cannot pin an old release because `index.html` is served with
`no-cache`; hashed assets are immutable and may remain cached.

## Published image tags

Three tags are published to `ghcr.io/datavisionzero`, and only one of them is
meant for an installation:

| Tag | Moved by | For |
| --- | --- | --- |
| `0.1.0` | nothing, once published | what an installation pins |
| `latest` | a release tag, never a prerelease | an installation that accepts every release |
| `main` | every green trunk push | our own staging installation |

`main` is the trunk, which is to say a commit whose tests passed and nothing
more: no release notes, no version number, and no promise that the Integration
API contract it carries is one anybody has agreed to. Pointing an installation
at it means taking whatever was merged in the last few minutes. It exists so
that a staging environment can follow the trunk without a build step of its own.

Pin `PAYAFFE_VERSION` to a released version. A pull should upgrade when the
operator decides to, not when a tag moves.

## Services

Both Compose files define these concrete services:

- `db`: PostgreSQL product database.
- `migrations`: controlled EF Core migration runner, enabled through the
  `operations` profile.
- `api`: ASP.NET Core host for APIs, health endpoints, and the built Payer/Admin
  SPA.
- `worker`: .NET worker host running Payment expiration, Blockchain
  Observation, Reorg Monitoring, Rate Cache refresh, Webhook Delivery, and the
  operational metric snapshot.

The Admin MCP host is not a Compose service. It is a local `stdio` process
started by the agent client and is documented in [admin-mcp.md](admin-mcp.md).

## Process Topology

Background work runs in the `worker` service, and `api` sets
`Workers__RunInApiHost` to `false`. A single-container installation without a
worker service can set `PAYAFFE_RUN_WORKERS_IN_API_HOST=true` instead; the API
then registers the same workers itself.

Running both at once is safe but wasteful: worker leases prevent duplicate
processing, but every worker would poll its providers twice. Choose one host.

## Configuration

An installation copies [deploy/.env.example](../../deploy/.env.example) to
`.env` beside `deploy/compose.yaml`. Working on payaffe copies the root
[.env.example](../../.env.example) instead. Either way, replace the placeholder
database password before starting services.

Both files draw on the same set of variables, and the root example lists all of
them:

- `PAYAFFE_DB_NAME`
- `PAYAFFE_DB_USER`
- `PAYAFFE_DB_PASSWORD`
- `PAYAFFE_DB_PORT`
- `PAYAFFE_API_PORT`
- `PAYAFFE_PAYER_PAGE_BASE_URL`
- `PAYAFFE_DEPLOYMENT_ENVIRONMENT`
- `PAYAFFE_RELEASE`
- `PAYAFFE_INSTALLATION_MODE`
- `PAYAFFE_BLOCKCHAIN_OBSERVATION_MODE`
- `PAYAFFE_BLOCKCHAIN_OBSERVATION_BLOCKCHAIR_BASE_URL`
- `PAYAFFE_BLOCKCHAIN_OBSERVATION_BLOCKCHAIR_API_KEY_REFERENCE`
- `PAYAFFE_BLOCKCHAIN_OBSERVATION_BLOCKCHAIR_MAX_TRANSACTIONS_PER_ADDRESS_POLL`
- `PAYAFFE_BLOCKCHAIN_OBSERVATION_NOWNODES_API_KEY_REFERENCE`
- `PAYAFFE_TRUSTED_PROXIES`
- `PAYAFFE_ADMIN_AUTH_RATE_LIMIT_PERMIT_LIMIT`
- `PAYAFFE_ADMIN_AUTH_RATE_LIMIT_WINDOW`
- `PAYAFFE_INTEGRATION_API_RATE_LIMIT_PERMIT_LIMIT`
- `PAYAFFE_INTEGRATION_API_RATE_LIMIT_WINDOW`
- `PAYAFFE_WEBHOOK_DELIVERY_MAX_ATTEMPTS`
- `PAYAFFE_WEBHOOK_DELIVERY_RETRY_DELAY`
- `PAYAFFE_WEBHOOK_DELIVERY_RETRY_BACKOFF_MULTIPLIER`
- `PAYAFFE_WEBHOOK_DELIVERY_MAX_RETRY_DELAY`
- `PAYAFFE_WEBHOOK_DELIVERY_RETRY_JITTER_RATIO`
- `PAYAFFE_WEBHOOK_DELIVERY_LEASE_DURATION`
- `PAYAFFE_OBSERVATION_WORKER_ENABLED`
- `PAYAFFE_OBSERVATION_WORKER_POLL_INTERVAL`
- `PAYAFFE_OBSERVATION_WORKER_MAX_PAYMENTS_PER_POLL`
- `PAYAFFE_OBSERVATION_WORKER_LEASE_DURATION`
- `PAYAFFE_REORG_MONITORING_WORKER_ENABLED`
- `PAYAFFE_REORG_MONITORING_WORKER_POLL_INTERVAL`
- `PAYAFFE_REORG_MONITORING_WORKER_MAX_TRANSACTIONS_PER_POLL`
- `PAYAFFE_REORG_MONITORING_WORKER_LEASE_DURATION`
- `PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V1`
- `PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V2`
- `PAYAFFE_EXCHANGE_RATES_ENABLED`
- `PAYAFFE_EXCHANGE_RATES_BASE_URL`
- `PAYAFFE_EXCHANGE_RATES_API_KEY_REFERENCE`
- `PAYAFFE_EXCHANGE_RATES_CACHE_INTERVAL`
- `PAYAFFE_EXCHANGE_RATES_MAX_STALE_AGE`
- `PAYAFFE_EXCHANGE_RATES_REQUEST_TIMEOUT`
- `PAYAFFE_COINGECKO_API_KEY`
- `PAYAFFE_RUN_WORKERS_IN_API_HOST`
- `PAYAFFE_CLIENT_ERRORS_ENABLED`
- `PAYAFFE_CLIENT_ERROR_RATE_LIMIT_PERMIT_LIMIT`
- `PAYAFFE_CLIENT_ERROR_RATE_LIMIT_WINDOW`
- `PAYAFFE_LOGAFFE_URL`
- `PAYAFFE_LOGAFFE_TOKEN`
- `PAYAFFE_OTLP_ENDPOINT`
- `PAYAFFE_OPERATIONAL_METRICS_SNAPSHOT_INTERVAL`
- `PAYAFFE_WORKER_HEALTH_PROBE_INTERVAL`
- `PAYAFFE_WORKER_HEALTH_MAX_AGE`

Production secrets must not be committed. The static browser application
carries no installation-specific API URL. Browser errors are posted to this
installation's own API and scrubbed there
([ADR 0026](../adr/0026-an-error-is-an-entry-and-there-is-no-error-tracker.md)).
`PAYAFFE_INSTALLATION_MODE` is `live` by default and `test` for an
installation that simulates its payments
([ADR 0033](../adr/0033-a-test-installation-simulates-its-external-truth.md)).
The first host to start against an empty database records the mode, and every
host after that, including the Admin MCP host, refuses to start when it is
configured for the other one; a database that already held Payments before the
setting existed is recorded as `live`. Switching modes means starting against a
new database, so real and simulated Payments never share one.

Blockchain Observation mode configuration uses
`PAYAFFE_BLOCKCHAIN_OBSERVATION_MODE`. The accepted values are `none`,
`blockchair`, and `nownodes`. The default `none` mode keeps provider polling
disabled at the provider-adapter level. The `blockchair` mode polls active
Payment Addresses through Blockchair address dashboard endpoints for BTC, LTC,
and native ETH. `PAYAFFE_BLOCKCHAIN_OBSERVATION_BLOCKCHAIR_BASE_URL` defaults
to `https://api.blockchair.com`, and
`PAYAFFE_BLOCKCHAIN_OBSERVATION_BLOCKCHAIR_MAX_TRANSACTIONS_PER_ADDRESS_POLL`
bounds the number of latest address transactions or calls requested per poll.
The `nownodes` mode polls active Payment Addresses through NOWNodes BlockBook
v2 address and transaction endpoints for BTC, LTC, and native ETH.
`PAYAFFE_BLOCKCHAIN_OBSERVATION_NOWNODES_BTC_BASE_URL`,
`PAYAFFE_BLOCKCHAIN_OBSERVATION_NOWNODES_LTC_BASE_URL`, and
`PAYAFFE_BLOCKCHAIN_OBSERVATION_NOWNODES_ETH_BASE_URL` default to the
provider's BTC, LTC, and ETH BlockBook endpoints, and
`PAYAFFE_BLOCKCHAIN_OBSERVATION_NOWNODES_MAX_TRANSACTIONS_PER_ADDRESS_POLL`
bounds the number of latest address transaction IDs fetched per poll. When
`blockchair` or `nownodes` is selected, the
matching API key reference must use this form:

```text
configuration:BlockchainObservation:ProviderSecrets:<name>
```

Then provide the corresponding provider API key through the shared backend
environment, for example `BlockchainObservation__ProviderSecrets__blockchair`
in a non-versioned `.env` file or another operator-controlled secret source.
The `worker` service polls the provider, so it is the host that must be able to
resolve the key.
Do not add provider API keys to `.env.example`, Compose files, logs, Audit Log
entries, or provider observation history.
Reorg Monitoring uses `PAYAFFE_REORG_MONITORING_WORKER_ENABLED`,
`PAYAFFE_REORG_MONITORING_WORKER_POLL_INTERVAL`, and
`PAYAFFE_REORG_MONITORING_WORKER_MAX_TRANSACTIONS_PER_POLL`. It re-polls
completed Payment Addresses only while contributing Matching Blockchain
Transactions remain inside the configured per-currency Reorg Monitoring Depth.
Admin authentication mutations use
`PAYAFFE_ADMIN_AUTH_RATE_LIMIT_PERMIT_LIMIT` and
`PAYAFFE_ADMIN_AUTH_RATE_LIMIT_WINDOW` for the route-and-source-IP fixed-window
rate limit. The public Integration API uses
`PAYAFFE_INTEGRATION_API_RATE_LIMIT_PERMIT_LIMIT` and
`PAYAFFE_INTEGRATION_API_RATE_LIMIT_WINDOW` for the route, source IP, and
bearer-token-fingerprint fixed-window rate limit. Both partition by the source
address the host believes in, which behind a proxy is the proxy's until
`PAYAFFE_TRUSTED_PROXIES` names it.
Webhook Delivery retries use
`PAYAFFE_WEBHOOK_DELIVERY_MAX_ATTEMPTS`,
`PAYAFFE_WEBHOOK_DELIVERY_RETRY_DELAY`,
`PAYAFFE_WEBHOOK_DELIVERY_RETRY_BACKOFF_MULTIPLIER`,
`PAYAFFE_WEBHOOK_DELIVERY_MAX_RETRY_DELAY`, and
`PAYAFFE_WEBHOOK_DELIVERY_RETRY_JITTER_RATIO` to control the exponential
backoff policy. `RetryDelay` is the first retry delay, the multiplier is
applied per subsequent failed attempt, `MaxRetryDelay` caps the computed
delay, and `RetryJitterRatio` adds bounded random jitter.

The Blockchain Observation worker runs in the `worker` service and is enabled
by default there. It stays idle while `PAYAFFE_BLOCKCHAIN_OBSERVATION_MODE` is
`none`. `PAYAFFE_OBSERVATION_WORKER_POLL_INTERVAL` controls the scheduler
interval, and `PAYAFFE_OBSERVATION_WORKER_MAX_PAYMENTS_PER_POLL` bounds the
number of active Payments queried per poll.

Webhook Endpoint records must store a secret reference instead of a raw
secret. For Compose-based installations, use references in this form:

```text
configuration:Webhooks:EndpointSecrets:<name>
```

Then provide the corresponding secret through the shared backend environment,
for example `Webhooks__EndpointSecrets__checkout` in a non-versioned `.env`
file or another operator-controlled secret source. The `api` service validates
the reference when an Admin saves the Endpoint, and the `worker` service signs
the delivered events, so both hosts must be able to resolve it. Do not add real Webhook Endpoint
secrets to `.env.example`, Compose files, logs, Audit Log entries, or Webhook
Delivery history.

The Compose baseline exposes two non-versioned slots for the first partner:

- `PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V1` resolves from
  `configuration:Webhooks:EndpointSecrets:partner-v1`.
- `PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V2` resolves from
  `configuration:Webhooks:EndpointSecrets:partner-v2`.

The values stay in `.env`; the versioned example contains empty placeholders.
Additional endpoints can use additional server-side configuration mappings.


## Running More Than One Instance

Each scheduled worker claims a named lease in `app.background_worker_leases`
before it processes a batch and releases it when the batch ends, so only one
instance of a given worker runs at a time. Webhook Delivery claims individual
due events with `for update skip locked`, which lets several instances deliver
different events in parallel while no event is delivered twice.

A crashed instance does not block the system: its lease is bounded by
`LeaseDuration`, and another instance takes over once the lease expires.
`PAYAFFE_LIFECYCLE_WORKER_LEASE_DURATION`,
`PAYAFFE_OBSERVATION_WORKER_LEASE_DURATION`,
`PAYAFFE_REORG_MONITORING_WORKER_LEASE_DURATION`,
`PAYAFFE_RATE_REFRESH_WORKER_LEASE_DURATION`, and
`PAYAFFE_WEBHOOK_DELIVERY_LEASE_DURATION` default to two minutes each. The
lease has to outlive one batch run, so raise it for deployments with large
batches; startup validation rejects values below one second or above one hour.

`app.background_worker_leases` also records the last success, last failure,
last safe error code, and consecutive failure count per worker, which is the
first place to look when scheduled work appears stalled.

## Partner Credential And Webhook Rotation

Authenticated Admin endpoints support listing, creating, rotating, and
disabling Integration API Credentials. Create and rotate responses show the
raw bearer token exactly once. Only its SHA-256 verifier is stored. Rotation
replaces the verifier immediately, so the previous token stops authenticating
as soon as the operation succeeds. Update the partner system promptly; the MVP
does not provide an overlap window for Integration API bearer tokens.

Webhook Endpoint Admin operations store only a restricted secret reference.
To rotate without exposing the secret through the Admin API:

1. Generate a new high-entropy HMAC secret and place it in the non-versioned
   `PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V2` slot.
2. Restart the API and the worker so the new reference is resolvable, then
   verify readiness.
3. Configure the receiver to accept the new secret, retaining the old secret
   temporarily if the receiver supports overlap.
4. Use the Step-up-protected Webhook Endpoint secret-rotation operation with
   the current resource version and
   `configuration:Webhooks:EndpointSecrets:partner-v2`.
5. Confirm signed delivery, remove acceptance of the old secret, clear the old
   `.env` value, and restart the API and the worker.

Create, update, rotate, and disable operations require an authenticated Admin
Session, valid CSRF evidence, fresh Step-up, the current resource version, and
produce Audit Log entries. Never copy bearer tokens or Webhook Endpoint
secrets into Audit Log data, URLs, shell history, screenshots, or support
artifacts.

## First Admin Account

Apply migrations before bootstrapping the first Admin Account. Generate a
one command:

```sh
docker compose --profile operations run --rm migrations \
  bootstrap-admin --username admin@example.test
```

It prompts without echo for the password and its confirmation, refuses
redirected input and output, creates the first Admin Account only while no
Admin Account exists, and prints Recovery Codes exactly once. Store those codes
somewhere that is not this host before closing the terminal.

The account has no second factor, and that is the decision rather than an
omission ([ADR 0028](../adr/0028-the-second-factor-is-optional-and-enrolled-later.md)):
requiring one before an installation had a single account meant an operator had
to agree a secret between a secret store, an authenticator app and this command
before anything existed to attach it to. It signs in on its password until an
admin enrols one.

Do not put the password, the Recovery Codes, or the database connection string
in the command, shell history, Compose file, logs, or support artifacts.
Running the bootstrap command again is not an Admin lockout-recovery path and
is refused.

## CoinGecko Exchange Rates

The API uses CoinGecko as the Exchange Rate Source and persists fetched
EUR/USD rates for BTC, LTC, and native ETH in `app.rate_cache`.
`PAYAFFE_EXCHANGE_RATES_CACHE_INTERVAL` controls how long a fetched cache entry
avoids another provider call. If CoinGecko is unavailable,
`PAYAFFE_EXCHANGE_RATES_MAX_STALE_AGE` is the maximum age of the provider
observation that may still produce a Rate Lock. The defaults are five and
thirty minutes.

The Rate Cache refresh worker requests all six fiat/cryptocurrency pairs in
one provider call at startup and then at
`PAYAFFE_RATE_REFRESH_WORKER_INTERVAL`. It is enabled by default and can be
disabled with `PAYAFFE_RATE_REFRESH_WORKER_ENABLED=false`. A newly created
Payment projects the current pair availability into its Payment Options; the
Payer Page disables only the currencies without a usable cached rate.

CoinGecko operation without an API key is supported. When an installation uses
a Demo API key, set `PAYAFFE_COINGECKO_API_KEY` only in the non-versioned
`.env` file and set:

```text
PAYAFFE_EXCHANGE_RATES_API_KEY_REFERENCE=configuration:ExchangeRates:ProviderSecrets:coingecko
```

The key is sent through the `x-cg-demo-api-key` header. It is never stored in
the Rate Cache, Rate Locks, logs, Audit Log entries, or browser configuration.
Invalid cache intervals, stale-age bounds, request timeouts, refresh intervals,
base URLs, or secret references fail startup validation while Exchange Rates
are enabled.

## Non-Custodial Payment Address Sources

BTC and LTC use account-level extended public keys. Configure only an extended
public key; an extended private key, seed phrase, or private key is rejected
and must never be placed in `.env`.

Account-level means the node a wallet exports for one account,
`m/purpose'/coin'/account'` — for example `m/84'/0'/0'` for the first native
segwit Bitcoin account. payaffe derives the external chain itself, so the
address it hands out at index `i` is the address that account shows at receive
index `i`. Configure the account node, not the chain node below it and not the
master key.

```text
PAYAFFE_BTC_WATCH_ONLY_ENABLED=true
PAYAFFE_BTC_WATCH_ONLY_EXTENDED_PUBLIC_KEY=xpub...
PAYAFFE_BTC_WATCH_ONLY_NETWORK=mainnet
PAYAFFE_BTC_WATCH_ONLY_ADDRESS_TYPE=segwit

PAYAFFE_LTC_WATCH_ONLY_ENABLED=true
PAYAFFE_LTC_WATCH_ONLY_EXTENDED_PUBLIC_KEY=xpub...
PAYAFFE_LTC_WATCH_ONLY_NETWORK=mainnet
PAYAFFE_LTC_WATCH_ONLY_ADDRESS_TYPE=segwit
```

Both currencies take the account key as `xpub` on mainnet and `tpub` on
testnet; LTC additionally takes `Ltub` and `ttub`. A native segwit wallet
exports the same node as `zpub` and a p2sh-segwit one as `ypub`, and neither is
taken — the prefix is four version bytes that hold no key material, so
converting is re-encoding the same key, and the `ADDRESS_TYPE` above is what
decides whether a segwit or a legacy address is derived from it. A rejected key
is reported with the prefix it carries and the prefix the currency takes.

Which chain a key came from is not part of the value. A Bitcoin and a Litecoin
account key are the same string, so an LTC key configured as the BTC source is
accepted, and every BTC address derived from it then belongs to a wallet that
never watches the Bitcoin chain. Nothing can check this for the operator:
confirm against the wallet that the address at receive index 0 matches before
the first payment is taken.

`mainnet` and `testnet`, plus `segwit` and `legacy`, are supported. The
database persists only the next non-hardened child index and a SHA-256 source
fingerprint. It does not persist the extended public key. `StartingIndex`
applies only when the cursor is first created. Replacing a configured source
without an explicit migration is refused because resetting or mixing address
derivation can reuse addresses. Back up the external watch-only wallet export
separately from the product database.

Native ETH addresses are imported through the authenticated
`POST /api/admin/native-eth-address-pool/import` operation as an `addresses` JSON
array. The mutation requires CSRF evidence and recent Step-up authentication,
rejects malformed or duplicate addresses atomically, and records an Audit Log
entry. `GET /api/admin/native-eth-address-pool` reports unused, assigned, and
retired counts plus the configured low-capacity state. Assigned addresses are
never returned to the unused pool.

## Migrations

The `api` and `worker` hosts bring the schema up to date as they start
([ADR 0027](../adr/0027-migrations-apply-on-startup.md)). There is no step to
run between `pull` and `up`, and no order to remember between versions.

While a migration is being applied the host is already up: `/health/live`
answers, and `/health/ready` answers `503` until the schema is current. A
migration that cannot be applied stops the host with exit code `70`, so the
container restarts rather than serving against a schema it does not expect.

The `migrations` service is still there for an operator who wants the schema
applied before anything else is started, and it runs the same code the hosts do:

```sh
docker compose --profile operations run --rm migrations
```

It is also what creates the first admin; see
[credential-rotation.md](credential-rotation.md).

## Start

An installation runs the published images:

```sh
docker compose up -d
```

Working on payaffe builds them from the working tree instead:

```sh
docker compose up --build db api worker
```

Logs are delivered to a logaffe installation through `PAYAFFE_LOGAFFE_URL` and
`PAYAFFE_LOGAFFE_TOKEN`, which are set together or not at all. Traces and
metrics use `PAYAFFE_OTLP_ENDPOINT`; see [observability.md](observability.md).

## Upgrading

An upgrade is two steps, and the hosts apply the schema themselves
([ADR 0027](../adr/0027-migrations-apply-on-startup.md)):

```sh
docker compose pull
docker compose up -d
```

Back up the database before the `up`, not after it
([postgresql-backup-restore.md](postgresql-backup-restore.md)). There is no
downgrade, so that artifact is the rollback for a migration that fails on the
way up. Which version a pull moves to is
[the tag `PAYAFFE_VERSION` names](#published-image-tags).

## Ports

The default local ports are:

- `http://localhost:8080` for the web application and API host.
- `localhost:5432` for PostgreSQL, from the root Compose file only. The
  deployment file does not publish the database at all.

The deployment file binds the published port to `127.0.0.1`, because the
reverse proxy that terminates TLS belongs in front of them. An installation
that really is reached directly sets `PAYAFFE_BIND_ADDRESS=0.0.0.0` and accepts
that admin sign-in then travels in the clear.

Override `PAYAFFE_API_PORT` or `PAYAFFE_DB_PORT` in `.env` when a local port is
already in use. Keep `PAYAFFE_PAYER_PAGE_BASE_URL` aligned with the public
origin so newly created Payment links remain reachable.

## Health

The API host exposes:

- `GET /health/live`
- `GET /health/ready`

`/health/ready` checks PostgreSQL connectivity and returns only a safe status
string. It does not expose connection strings, credentials, payment data, or
raw configuration.

The worker host serves no HTTP endpoint. It writes a heartbeat file every
`PAYAFFE_WORKER_HEALTH_PROBE_INTERVAL`, but only while it can still reach the
database, and its container healthcheck runs the same executable again:

```sh
docker compose exec worker dotnet Payaffe.Worker.dll health
```

The command exits `0` while the heartbeat is younger than
`PAYAFFE_WORKER_HEALTH_MAX_AGE` and `1` otherwise, with a safe reason on
stderr. The maximum age has to stay longer than the probe interval; startup
validation rejects a configuration where it does not.

The heartbeat is per process on purpose. Lease rows in
`app.background_worker_leases` are database-wide, so a second healthy instance
would keep them fresh and make a wedged instance look alive. Whether scheduled
work is actually progressing is an alerting question, not a liveness question.

The Compose `api`, `worker`, and `web` services all define container
healthchecks.

## Backups

The `db-data` Docker volume is mounted at `/var/lib/postgresql` for the
PostgreSQL 18 container layout and contains the PostgreSQL product database.
It is the primary backup target for this Compose baseline.

[postgresql-backup-restore.md](postgresql-backup-restore.md) is the full
backup and restore runbook, including the restore verification checklist. The
short form is:

```sh
mkdir -p backups
docker compose exec -T db sh -c \
  'pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom' \
  > "backups/payaffe-$(date -u +%Y%m%dT%H%M%SZ).dump"
```

Protect backup files like production authentication data. Define encrypted
off-host storage, retention, and restore-test frequency for the installation.
Back up the non-versioned `.env`, reverse-proxy/TLS configuration, and external
watch-only wallet exports separately. Never add wallet seeds or spending keys
to the product backup.

Test restoration into a separate database before relying on a backup:

```sh
docker compose exec -T db sh -c \
  'createdb --username "$POSTGRES_USER" payaffe_restore_test'
docker compose exec -T db sh -c \
  'pg_restore --username "$POSTGRES_USER" --dbname payaffe_restore_test --clean --if-exists' \
  < backups/payaffe-YYYYMMDDTHHMMSSZ.dump
```

Verify Payment and Webhook Event counts, Integration API Credential status,
Audit Log continuity, Watch-Only Wallet cursor positions, and native ETH
assigned/unused counts. In particular, no assigned native ETH address may
become unused after restoration. Drop the isolated restore-test database only
after verification.

## Small-Value Pilot Smoke Test

[`scripts/release-smoke.sh`](../../scripts/release-smoke.sh) automates the
deployment-level checks against a throwaway installation. It uses its own
Compose project name and ports, so it never touches this installation. Run it
first, then walk the value-carrying steps below against the real installation.

Before accepting normal traffic:

1. Confirm `/health/ready` is healthy and that the `worker` service started
   without configuration-validation errors. An invalid worker configuration
   exits the container with code 78.
2. Bootstrap the first Admin, create one Integration API Credential, and
   create a Webhook Endpoint whose receiver verifies the signature and
   deduplicates by `event_id`.
3. Confirm CoinGecko cache entries and BTC/LTC Watch-Only Wallet Sources or
   native ETH Address Pool capacity are available.
4. Create a Payment for the installation's smallest practical fiat amount
   using bearer authentication and an `Idempotency-Key`; repeat the request and
   confirm the same `payment_id` is returned.
5. Select one currency on the Payer Page, verify the address against the
   operator-controlled wallet, and send a small on-chain amount.
6. Confirm the Payment progresses through observation and completion, the
   signed Webhook Event is accepted exactly once by the receiver, and
   `GET /api/v1/payments/{paymentId}` reconciles to the same final state.
7. Inspect Observation Health, Audit Log, Webhook Delivery attempts, and any
   Reorg Alerts. Resolve every unexplained warning before increasing value or
   traffic.
