# Setting Up an Installation With an Agent

This is the procedure for an agent that installs payaffe on a host for a person.
Every step runs without a terminal, and the agent never learns an Admin
password or a Recovery Code. The reference for each setting is
[docker-compose.md](docker-compose.md); this page is the order and the
decisions.

## 1. Ask first, in one go

Two answers cannot be changed later, and a third decides what else is needed.
Ask the person all of them before creating anything:

- **Live or test.** A live installation takes real payments. A Test Mode
  installation simulates addresses, exchange rates and incoming payments, so an
  integration can be developed without a wallet, a provider account or any
  money ([test-mode.md](test-mode.md)). The first start writes the mode into
  the database, and a host configured for the other mode refuses to start
  against it: switching later means a new, empty database. When the person is
  not sure, test is the safe answer, and a live installation is set up beside
  it later.
- **The Admin username**, for example their email address. It is fixed for the
  first account.
- **The public address**, such as `https://pay.example.com`, and whether a
  reverse proxy already terminates TLS on this host. A test installation used
  only from this machine can stay at `http://localhost:8080`.

A live installation also needs, and only the person can supply:

- the blockchain provider (`blockchair` or `nownodes`) and its API key;
- for BTC and LTC, the account-level extended public key of the receiving
  wallet (`xpub`, `Ltub`), never a private key or a seed;
- for native ETH, the list of receiving addresses to import.

A test installation needs none of those.

## 2. Files

In a directory of its own, so it gets its own database volume:

```sh
mkdir payaffe && cd payaffe
base=https://raw.githubusercontent.com/datavisionzero/payaffe/main/deploy
curl -fsSO "$base/compose.yaml"
curl -fsS -o .env "$base/.env.example"              # live
# curl -fsS -o .env "$base/test-mode.env.example"   # test
chmod 600 .env
```

## 3. Configuration

Write secrets into `.env` without printing them, so they never reach the
transcript (GNU `sed`; on macOS it is `sed -i ''`):

```sh
sed -i "s|^PAYAFFE_DB_PASSWORD=.*|PAYAFFE_DB_PASSWORD=$(openssl rand -hex 32)|" .env
sed -i "s|^PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V1=.*|PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V1=$(openssl rand -hex 32)|" .env
```

Then set `PAYAFFE_PUBLIC_URL`. For a live installation also pin
`PAYAFFE_VERSION` to the newest release (the comment in `.env` says how to find
it), and set the provider, its key, and the wallet sources the person gave. The
comments in `.env` describe each value; an invalid one stops the hosts at start
with the setting named.

## 4. Start

```sh
docker compose up -d --wait
```

It returns once every healthcheck passes. `docker compose logs api worker`
names the setting that failed when it does not.

Behind a reverse proxy, set `PAYAFFE_TRUSTED_PROXIES` now that the network
exists ([docker-compose.md](docker-compose.md#telling-the-api-who-the-caller-is))
and run `docker compose up -d --wait` again.

## 5. The first Admin

```sh
mkdir -m 700 first-admin
docker compose --profile operations run --rm -T \
  --user "$(id -u):$(id -g)" \
  --volume "$PWD/first-admin:/first-admin" \
  migrations bootstrap-admin \
  --username <username from step 1> \
  --credentials-file /first-admin/credentials.txt
```

It prints the Admin Account id and where the file is, and nothing secret. The
password is generated; the file holds it and the Recovery Codes, readable by
its owner only ([ADR 0037](../adr/0037-the-first-admin-can-be-written-to-a-file-for-an-unattended-setup.md)).
The `--user` makes that owner the account the agent runs as, rather than the
container's own user, which cannot write the mounted directory. The command
refuses a file that already exists and refuses to run once any Admin Account
exists.

**Do not read the file.** Its content would enter the transcript. Tell the
person the full path on the host, for example:

> The first Admin Account is created. Its password and Recovery Codes are in
> `/opt/payaffe/first-admin/credentials.txt`, readable only by `deploy`.
> Open it in a terminal on the host (`cat` is enough), sign in at
> `https://pay.example.com/admin`, move the password and the Recovery Codes into
> your password manager, and then delete the file and the directory. Adding a
> second factor in the Admin UI is recommended.

## 6. What only the person does

Everything protected by Step-up needs the Admin's own sign-in: creating the
Integration API Credential for their shop and the Webhook Endpoint that points
at it. On a live installation they also confirm, in their wallet, that the
first BTC and LTC address payaffe derives is the wallet's receive address at
index 0 before the first payment is taken
([docker-compose.md](docker-compose.md#non-custodial-payment-address-sources)).

Native ETH addresses can be imported by the agent through the Admin MCP host,
which acts as the Admin Account id printed in step 5 ([admin-mcp.md](admin-mcp.md)).

## 7. Hand over

Report the installation's address, its mode, the pinned version, where `.env`
and the credentials file are, and which of the steps in 6 are still open. Point
at [postgresql-backup-restore.md](postgresql-backup-restore.md): `.env` and the
database volume are what a backup has to cover.
