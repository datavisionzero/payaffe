# The Backend Owns Every Protected Mutation

One Next.js application under `apps/web` serves both the payer page and the
Admin UI. It owns routing, rendering, UI state, forms, accessibility,
localization, and error presentation. It does not own a single rule about when a
payment is paid, and it does not mutate anything without going through the .NET
API.

Server Actions are the specific thing being refused. They are the idiomatic
Next.js answer and they would put a protected mutation in the frontend
codebase — where it would be reviewed as frontend code, tested with frontend
tools, and separated from the authorization, audit, and outbox behaviour that
every payment change is required to have
([ADR 0013](./0013-a-payment-change-and-the-events-it-causes-share-one-transaction.md)).
The rule that keeps that from happening has to be simple enough to apply without
thinking: protected mutations are API calls, always.

Splitting payer and admin into two applications was considered and rejected as
premature. They share components, tooling, and a deployment; one boundary that
is enforced (frontend against backend) is worth more here than two that are
merely present.

## Consequences

**API clients are generated from the backend contract, never hand-written.**
`apps/web/lib/api/generated.ts` is produced from the OpenAPI description and CI
fails when the committed copy has drifted, which is what stops the frontend's
idea of the API from diverging from the API.

**TanStack Query owns server state.** Caching, loading, polling — the payer page
polls for status — and invalidation live there, and React local state is
reserved for state that is genuinely local to a component.

**Browser storage holds nothing sensitive.** No bearer tokens, session secrets,
CSRF secrets, MFA secrets, recovery codes, webhook secrets, provider keys, or
payment payloads. The admin session is an `HttpOnly` cookie precisely so that
the frontend cannot read it
([ADR 0019](./0019-the-second-factor-is-totp-and-sensitive-writes-need-a-fresh-one.md)).

**The frontend is a Node runtime the deployment has to carry**, on top of the
.NET one. That is the honest cost of a React frontend here, and it was accepted
in exchange for one component model across a public payer page and an
authenticated admin surface.

The detailed rules are in
[frontend-baseline.md](../architecture/frontend-baseline.md).
