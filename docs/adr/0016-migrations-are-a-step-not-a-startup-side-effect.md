# Migrations Are a Step, Not a Startup Side Effect

Schema changes are applied by the `migrations` service, run deliberately. The
`api`, `worker`, `mcp`, and `web` hosts never apply a migration while starting
up, even though EF Core makes doing so a single line.

That single line is the alternative, and it is attractive right up to the moment
it matters. Migrating on startup means the schema changes when a container
restarts — which is to say at the least predictable moment available, possibly
during an unrelated incident, possibly in several replicas at once, and always
without anyone having decided that now was the time. It also merges two failures
that need different responses: a host that cannot start and a migration that
cannot apply.

Separating them costs one documented step in an upgrade and buys an upgrade that
can be stopped after the schema and before the code, or the reverse.

## Consequences

**The upgrade order is part of the operational documentation**, not folklore:
pull, run `migrations`, then start the rest. It is written down in
[docker-compose.md](../operations/docker-compose.md) because an operator doing
this twice a year will not remember it.

**A migration that fails leaves the old code running.** That is the point — the
previous version is still up and serving, and the operator has a decision to
make rather than a crash loop to interpret.

**The first admin is created the same way, and for the same reason.** It is an
explicit operations command after migrations rather than a thing a host does on
first boot; see
[ADR 0020](./0020-the-first-admin-is-created-by-a-local-command.md).

**Health and readiness say nothing about the schema.** `/health/live` and
`/health/ready` report whether the host can serve, and they never expose
connection strings, configuration, payment data, or provider payloads — a
readiness endpoint is reachable by whoever can reach the port.
