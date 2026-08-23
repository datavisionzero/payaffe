# PostgreSQL Backup And Restore

`payaffe` keeps every durable record in PostgreSQL: Payments, Payment Event
History, Rate Locks, Payment Address assignments and derivation cursors, the
native ETH Address Pool, Matching Blockchain Transactions, Reorg Alerts, the
Webhook Outbox and its delivery history, Integration API Credentials, Admin
Accounts, and the Audit Log. The database is therefore the only stateful
component that has to be backed up.

The product is non-custodial. No spending key exists in the database or in any
backup, and none must ever be added to one.

This runbook uses custom-format logical backups. The operator owns the schedule,
encrypted storage, access control, retention, and restore drills.

## Backup

```sh
docker compose exec -T db pg_dump \
  --username "$PAYAFFE_DB_USER" \
  --dbname "$PAYAFFE_DB_NAME" \
  --format=custom --no-owner --no-acl \
  > payaffe-$(date -u +%Y%m%dT%H%M%SZ).dump
```

Record the application version, the applied migration level, the UTC timestamp,
the dump checksum, and the operator who took it. Encrypt the dump before it
leaves the host.

Back up separately, outside the database:

- the non-versioned server-side secret source (`Admin:TotpSecrets`,
  `Webhooks:EndpointSecrets`, `BlockchainObservation:ProviderSecrets`,
  `ExchangeRates:ProviderSecrets`),
- BTC and LTC Extended Public Keys,
- TLS material and the deployment configuration.

Without those, a restored database cannot resolve secrets or derive addresses.

## Restore Drill

Restore into a new, empty database. Never overlay a database that is in use.

```sh
docker compose stop api web worker
docker compose exec -T db createdb --username "$PAYAFFE_DB_USER" payaffe_restore
docker compose exec -T db pg_restore \
  --username "$PAYAFFE_DB_USER" \
  --dbname payaffe_restore \
  --no-owner --no-acl --exit-on-error \
  < payaffe-<timestamp>.dump
```

Stopping `worker` matters as much as stopping `api`: a running worker keeps
taking leases, polling providers, and delivering Webhook Events against whatever
database it is pointed at.

Point a non-production API instance at the restored database — and note that
starting one applies migrations to whatever it is pointed at
([ADR 0027](../adr/0027-migrations-apply-on-startup.md)). Restoring into an
older application version than the dump came from is the case to be careful
with: start a host of the version that took the dump, or apply the schema
deliberately first and read the result before starting anything.

```sh
docker compose --profile operations run --rm migrations
```

## Verification Checklist

- `/health/ready` succeeds against the restored database.
- The applied migration level matches the recorded application version.
- Payment counts and a representative Payment's Event History agree with the
  source.
- Webhook Outbox status counts, delivery attempts, and terminal failures agree.
- Integration API Credentials, Webhook Endpoints, Admin Accounts, Admin
  sessions, and Audit Log counts agree.
- BTC and LTC derivation cursors are preserved, including their source
  fingerprint, so no address can be reissued to a second Payment.
- Native ETH Address Pool rows are preserved and no `assigned` address has
  returned to `unused`.
- Observation Health, Reorg Alerts, and `app.background_worker_leases` rows are
  present.
- No secret value appears in the restore logs or the backup metadata.

Delete the drill database once the evidence is recorded.

## Production Restore

A production restore needs a declared maintenance window, a named incident
owner, an explicit rollback decision, and a fresh backup of the failed database
before it is replaced. Follow [incident-response.md](incident-response.md) for
the surrounding process.

Re-assigning an address that a Payer may already have paid to is the one
outcome a restore must never produce. When in doubt about the age of a backup
relative to Payment Address assignments, retire the affected addresses rather
than letting them be reissued.
