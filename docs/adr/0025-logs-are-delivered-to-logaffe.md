# Logs Are Delivered to logaffe, and Only Logs

Every host writes structured JSON to its console, and from there its log entries
go to a [logaffe](https://github.com/datavisionzero/logaffe) installation
through `Logaffe.Extensions.Logging`, an `ILoggerProvider` on nuget.org. Traces
and metrics continue to leave over OTLP, and error reports continue to go to
GlitchTip.

The path this replaces was OTLP for logs as well: Grafana Alloy into Loki,
alongside the traces and metrics. It worked, and it asked an operator taking
fifteen payments a day to run a log aggregation stack in order to read what
their payment service said. logaffe is a self-hostable logging tool sized for
one operator, which is the same person this product is sized for
([ADR 0017](./0017-one-installation-serves-one-shop.md)).

The split is not a preference, it is what each target accepts. logaffe takes log
entries and neither spans nor time series, so traces and metrics could not move
even if we wanted them to. GlitchTip groups errors across releases and keeps
stack traces, which logaffe does not attempt: its notifications are a closed set
of three conditions about the installation itself — the store filling up, an
application that stopped delivering, a project whose volume jumped — and its own
documentation is explicit that it is not an alerting system. Dropping GlitchTip
would have lost error grouping and gained nothing.

## Consequences

**Three channels instead of two, and all three optional.** An installation that
configures none of them still has `docker compose logs`, which stays the
fallback. Nothing about this makes central logging mandatory.

**A lost entry is affordable, and that is by design.** The client holds a
bounded in-memory queue, drops the oldest entries when it is full, never blocks
or throws into the host, and flushes on shutdown with a timeout. There is no
durable buffer and no retry that outlives the process. That is acceptable
precisely because the console log is still there and is not going anywhere — an
unbounded queue would turn a logging outage into a payment outage, which is a
worse trade than losing a line.

**Half a configuration fails at startup.** An address without a token, or a
token without an address, throws before the host runs. A wrong OTLP endpoint
merely leaves a dashboard empty, but logs that were never delivered are missed
at the moment somebody is trying to reconcile a payment.

**The ingest token is a secret, and it is also the project selector.** It names
where entries land, so it lives in the environment like every other secret
([ADR 0021](./0021-secrets-live-in-the-environment-never-in-the-repository.md))
and is issued per installation.

**Entries correlate with spans without the application arranging it.** The
provider reads `Activity.Current` for the trace and span, and is configured with
the same instance identifier as the `service.instance.id` resource attribute, so
an entry in logaffe and a span in Tempo agree on which replica produced them.

**payaffe now depends on a package we also publish.** It is MIT, on nuget.org,
and pinned centrally like everything else, so this is an ordinary dependency and
not a private coupling — but it is worth naming, because a decision to keep a
sibling product healthy is not the same kind of decision as choosing a library.
