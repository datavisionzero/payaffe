# A Payment Change and the Events It Causes Share One Transaction

When a payment moves — observed, completed, expired, settled — the state change,
its Payment Event History entry, the webhook outbox record, the audit entry if
there is one, and any follow-up work are all written in the same database
transaction. Either all of it happened or none of it did.

The alternative is to change the state and then send the webhook, and it is
wrong in a way that only shows up later: the process dies between the two, the
payment is complete in the database, and the shop is never told. There is no
retry to schedule because nothing recorded that a send was owed. The outbox
turns "we must remember to tell someone" into a row, and a row survives a crash.

Provider calls stay outside the transaction. Blockchain APIs, exchange rates,
and webhook endpoints are network calls with network timeouts, and holding a
database transaction open across one of them is how a slow provider becomes a
lock queue.

## Consequences

**The worker is what closes the loop.** It reads outbox rows and delivers them,
which is why delivery is at-least-once
([ADR 0010](./0010-a-webhook-is-signed-versioned-and-at-least-once.md)) — the row
is only marked delivered after the call returned, and a crash in between means
the call happens twice.

**Work must be idempotent, and this is not optional.** Every worker path has to
be safe to run again on the same row, because "again" is the normal case rather
than the exception.

**Claiming is atomic and leases expire.** Two worker instances must not process
the same delivery, and a worker that dies holding a claim must not hold it
forever — which is what the `*_LEASE_DURATION` settings bound.

**Nothing critical rides on an in-memory timer.** Expiration, observation
polling, rate refresh, confirmation updates, reorg monitoring, and retries all
persist their due work or their scheduler state, so a restart resumes rather
than forgets.

The detailed rules are in
[background-jobs-outbox-scheduler-baseline.md](../architecture/background-jobs-outbox-scheduler-baseline.md).
