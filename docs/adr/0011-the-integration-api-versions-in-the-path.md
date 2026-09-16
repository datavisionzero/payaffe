# The Integration API Versions in the Path

ADR 0031 adds an authenticated, idempotent Currency Selection endpoint and
additive Payment fields to v1. The initial two-endpoint description below is
historical; the path-versioning and compatibility decision remains current.

Everything under `/api/v1/` is public contract. A breaking change means
`/api/v2/`, not an edit. Errors are `ProblemDetails` with a `correlationId` and
a stable machine-readable code; validation failures use one shape,
`validation.failed`, with field paths and codes in an `errors` extension.

The version was put in before there was anything to version, which is the whole
point — versioning is nearly free at design time and nearly impossible to
retrofit once shops depend on unversioned paths. The Integration API is the one
surface in this product that outsiders build against, so it is the one surface
where an accidental contract would be expensive.

`ProblemDetails` rather than a bespoke error envelope, because the failure modes
here are ordinary HTTP ones and inventing a shape for them buys nothing an
integrator wants.

## Consequences

**OpenAPI is generated from the implementation and diffed in CI.** The accepted
description lives at `docs/contracts/integration-api/openapi.v1.json`, a test
regenerates it, and a change that was not intended fails the build instead of
shipping. This is what makes "everything under `/api/v1/` is contract" a
mechanism rather than a promise.

**The initial surface is deliberately two endpoints.** `POST /api/v1/payments`
creates, `GET /api/v1/payments/{paymentId}` reads. No list endpoint was added to
satisfy a convention, and when one is added it will need to decide cursor
pagination on its own merits.

**Idempotency is a required header, not an optional courtesy.**
`Idempotency-Key` is scoped to the calling credential: the same key with the
same body returns the same payment, and the same key with a different body is a
conflict. A shop that retries a creation after a timeout gets one payment, which
is the failure this product cannot afford to get wrong.

**`ETag` and `If-Match` were absent because nothing initially mutated.** Create
and read need no optimistic concurrency. ADR 0031 gives Currency Selection a
narrower first-write-wins rule; any other mutating endpoint still has to decide
its concurrency behaviour before introduction.

The detailed contract rules are in
[integration-api-contract.md](../architecture/integration-api-contract.md).
