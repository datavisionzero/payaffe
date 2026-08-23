# The Schema Is Split by What It Would Cost to Leak

There are four schemas and none of them is `public`. `app` holds the payment
domain and product configuration, `auth` holds admin authentication material and
integration credential hashes, `audit` holds the security audit log, and
`outbox` holds durable jobs, scheduler state, webhook delivery, and dead
letters.

One `app` schema for everything was the alternative, and for a database this
size it is defensible on ergonomics alone. What decided against it is that these
four groups have genuinely different rules. Audit entries are append-only and
retained for at least 180 days. Auth rows are the ones that must never appear in
a diagnostic query someone pastes into a chat. Outbox rows are the ones that get
deleted on a retention schedule. Domain rows are the ones a support question is
actually about. Keeping them in one namespace means every one of those rules is
enforced by remembering, and a schema boundary is a thing a reviewer can see.

`public` is left empty deliberately, so that a table which ends up there is
visibly a mistake rather than a default.

## Consequences

**Histories live with their subject, not with each other.** Payment Event
History is domain history and belongs in `app`; webhook delivery history belongs
with delivery processing in `outbox`; the security audit log is `audit`. These
three get conflated constantly in conversation and are three different things
with three different retentions.

**Naming is lowercase `snake_case`, and identifiers that cross the API boundary
are UUIDs.** Sequence numbers, positions, and attempt counters may be narrower
types precisely because they are not stable identifiers and must not leak into a
contract.

**Time is `timestamptz` and always UTC.** A `date` column means a date, not a
midnight — and where that distinction was blurred it produced the kind of
off-by-one that is discovered in a different timezone.

**Mutable domain rows carry `created_at`, `updated_at`, and `version`.** The
`version` column is what a future `ETag` would be derived from, and the raw
value stays internal rather than becoming something a client parses.

The full conventions are in
[database-conventions-baseline.md](../architecture/database-conventions-baseline.md).
