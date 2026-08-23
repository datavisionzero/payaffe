# Migrations Apply on Startup

Supersedes [ADR 0016](./0016-migrations-are-a-step-not-a-startup-side-effect.md).

The `api` and `worker` hosts bring the schema up to date while they start. An
upgrade is `docker compose pull` and `docker compose up -d`, with no step
between them that somebody has to remember or a script has to encode.

ADR 0016 decided the opposite, and its argument was not wrong: migrating on
startup means the schema changes at a moment nobody chose, and it merges a host
that cannot start with a migration that cannot apply. What changed is who runs
the upgrade. That decision was written for an operator doing this twice a year
by hand, for whom one documented step is cheap. It is expensive for a deployment
that runs unattended, because the step then lives somewhere other than the
product — in a timer, a pipeline, or a runbook — and the thing that applies the
schema stops being the thing that ships with the code that needs it. An upgrade
procedure that only exists in the operator's automation is an upgrade procedure
that drifts from the version it is upgrading.

So the deliberate act moves rather than disappears. It is the decision to run
the new images at all — pushing a tag, or letting a trunk build reach an
installation that follows one — and that decision is made before anything is
pulled, by somebody, on purpose.

## Consequences

**A migration that fails is a crash loop, and the old version is not left
serving.** This is the cost ADR 0016 declined to pay, named plainly. What it
buys back is that a failure is loud and immediate instead of being a difference
between two containers that nobody looks at. The rollback is the backup taken
before the deployment, which is what it already was: there is no downgrade.

**Readiness reports the schema, not just the connection.** `/health/ready`
answers `not_ready` until the migration has finished. Under ADR 0016 health said
nothing about the schema, which was honest when the schema was applied
separately; with the migration inside the startup path, an endpoint that only
asked whether PostgreSQL answered would report an installation as serving while
half a schema was in place. It still reports only `ready` or `not_ready` — which
migration is pending is not something a readiness endpoint tells whoever can
reach the port.

**The migration does not block the host from starting.** It runs in the
background while the host comes up, because the alternative fails in the wrong
direction: an API that refused to start until PostgreSQL answered would take
`/health/live` down with it, and a liveness endpoint that fails when a
dependency does is not a liveness endpoint. So the host starts, serves
`/health/live`, and answers `not_ready` until the schema is current.

**A database that is not there yet is waited for; a migration that cannot apply
is fatal.** They are different failures and had to stop being one. The first is
ordinary — on a first `docker compose up` PostgreSQL is routinely a few seconds
behind — and it is met with a backoff that keeps waiting, because something is
already watching how long readiness takes. The second exits the host with
`EX_SOFTWARE`.

**The scheduled workers wait for the schema rather than failing into it.** They
derive from a base class that awaits the migration before its first tick. Without
it, every cold start would produce a burst of failures against tables that do not
exist yet — and an error is an entry
([ADR 0026](./0026-an-error-is-an-entry-and-there-is-no-error-tracker.md)), so
those entries would be indistinguishable from the ones worth reading.

**Both hosts migrate, and an advisory lock is what makes that safe.** They are
started by one `docker compose up` and either may win. Whoever takes
`pg_advisory_lock` applies the schema; the other blocks, then finds nothing
pending and carries on. Designating one host as the migrator was the
alternative, and it only moves the problem: the other host then needs to wait
for a schema it has no way to ask about. This is also what makes
`PAYAFFE_RUN_WORKERS_IN_API_HOST` harmless either way.

**The `migrations` service stays.** It is no longer part of an upgrade, but it
still carries `bootstrap-admin` ([ADR 0020](./0020-the-first-admin-is-created-by-a-local-command.md)),
and `migrate` remains available for an operator who wants the schema applied
before anything is started. It runs the same code the hosts run, so there is no
second implementation to drift.

**The first admin is unaffected.** ADR 0020 refuses a first-run registration
page on its own merits, not because migrations were a separate step, and
nothing here changes that: the first Admin Account is still created by an
interactive local command that prompts for a password and verifies a TOTP code.
