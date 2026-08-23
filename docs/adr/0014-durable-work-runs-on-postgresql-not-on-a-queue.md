# Durable Work Runs on PostgreSQL, Not on a Queue

Jobs, scheduler state, and the outbox are tables. There is no Redis, no
RabbitMQ, and no cloud queue, and a .NET worker host polls those tables with an
atomic claim.

A queue is the better tool for this shape of work in general, and it is refused
on volume. Five to twenty payments a day generates a handful of outbox rows an
hour; a broker sized for that is a service to install, secure, monitor, back up,
and upgrade in exchange for throughput nobody will use. It would also break the
property [ADR 0013](./0013-a-payment-change-and-the-events-it-causes-share-one-transaction.md)
depends on — an outbox row and the payment change that produced it are in one
transaction precisely because they are in one database, and a broker would put
the send on the far side of a boundary the transaction cannot cross.

PostgreSQL is already there for payment state, so this spends nothing from the
service budget of
[ADR 0001](./0001-deployment-is-compose-and-the-service-count-is-the-budget.md).

## Consequences

**Polling intervals are the tuning surface, and they are configuration.** Each
worker has its own interval, batch size, and lease duration —
`PAYAFFE_OBSERVATION_WORKER_POLL_INTERVAL` and its siblings — because the right
value differs per workload and per provider rate limit.

**Latency has a floor equal to the poll interval.** A webhook is not sent the
instant the payment completes; it is sent on the next tick. At a ten-second
delivery interval that is invisible to a shop and would be unacceptable at a
different scale.

**The tables have to be kept from growing forever.** Delivered outbox rows,
completed jobs, and dead-lettered records need retention, and that retention is
itself scheduled work rather than a manual chore.

**This is the decision volume reopens.** If payment volume ever outgrows table
polling, a broker becomes correct and this ADR is the one to supersede — with
the outbox staying in PostgreSQL and the broker taking over only the delivery
side, so the transactional property survives the change.
