# Deployment Is Compose, and the Service Count Is the Budget

The service roster is amended by
[ADR 0030](./0030-the-web-application-is-a-vite-spa-served-by-the-api.md): the
web application is now static output in the API image, not a long-running
`web` service.

An installation is a compose file, a `.env`, and a PostgreSQL volume. The
obvious alternative is Kubernetes, and it is not refused because it is bad — it
is refused because the operator this product is for runs one shop, takes five to
twenty payments a day, and would spend more time on the orchestrator than on the
payments it orchestrates.

The decision that actually bites is the second half of the title. Compose makes
adding a service almost free at the moment of writing it and permanently
expensive for whoever installs it, because every service is one more thing that
can be misconfigured, fail to start, or be forgotten during an upgrade. So the
service count is treated as a budget rather than an outcome: `db`, `migrations`,
`api`, `worker`, and an `mcp` host that is not part of the running stack. A new
long-running service is a decision, not a refactoring.

## Consequences

**A queue, a cache, and a search engine are all things this product does not
have.** Each would be defensible on its own merits and each would spend the
budget. Where one of them looks necessary, PostgreSQL is tried first — which is
what [ADR 0014](./0014-durable-work-runs-on-postgresql-not-on-a-queue.md) is
about, and that ADR is where the argument belongs rather than here.

**The worker is a separate service anyway.** It is the one exception that was
worth paying for: blockchain polling, webhook delivery, and rate refresh all
have failure modes that should not be able to take the API down with them, and
`PAYAFFE_RUN_WORKERS_IN_API_HOST` exists so a single-container installation can
still collapse the two when it wants to.

**Scaling out is not the story.** One installation serves one operator
([ADR 0029](./0029-one-operator-can-isolate-payment-projects.md)) at a volume
that fits on one host. If that stops being true, this is the decision to reopen
— and it should be reopened as a question about volume, not answered quietly by
adding a replica count to a compose file.

The operational rules that follow from this — health endpoints, backup targets,
restore behaviour — are recorded in
[deployment-operations-baseline.md](../architecture/deployment-operations-baseline.md).
