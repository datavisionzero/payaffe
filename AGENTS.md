# AGENTS.md

This file gives coding agents the default operating rules for `payaffe`.

## Project Context

`payaffe` is an open-source, self-hostable crypto payment application for low-volume payment flows.

The MVP is single-tenant: one deployment serves one operator or shop. The MVP currency scope is BTC, LTC, and native ETH. Payment creation starts from EUR or USD fiat amounts. The product is non-custodial and must not store spending keys, perform refunds, sweeps, or withdrawals.

The product exposes:

- a web frontend,
- an Integration API,
- an admin MCP surface for selected agent-assisted operations.

## Working Style

- Start with [VISION.md](VISION.md) for what the product is and is not, then [docs/README.md](docs/README.md) for the documentation map.
- Agent-facing conventions for this repository live in [docs/agents/](docs/agents/).
- Follow [docs/architecture/technical-constraints.md](docs/architecture/technical-constraints.md), [docs/architecture/observability-baseline.md](docs/architecture/observability-baseline.md), and [docs/architecture/repository-structure.md](docs/architecture/repository-structure.md) for technical and structural baselines.
- Use [CONTEXT.md](CONTEXT.md) for domain language. Do not invent alternate names for established terms.
- Architecture decisions live in [docs/adr/](docs/adr/). Follow the form described in [docs/adr/README.md](docs/adr/README.md) and update the decision index there.
- Documentation clarifies intent, domain language, and decisions. Code and tests remain the technical source of truth, and documentation should not retell them.
- Treat GitHub Issues as public. Create or update one only when the work is
  intended for public discussion; otherwise report the follow-up in the handoff
  so a maintainer can route it appropriately.
- Do not create target folders, toolchain files, or CI jobs in advance when they do not have a concrete use yet.

## Working Language

- All repository content is written in English: documentation, comments, commit
  messages, and pull request text.
- Keep implementation comments and commit text concise and technical.

## Git And GitHub

- This project is hosted on GitHub. `gh` is part of the normal development
  workflow; use it for public issues, pull requests, and CI checks when practical.
- Development is trunk-based. `main` is the only long-lived branch and stays
  deployable; do not create `develop`, `release`, or Git Flow branches.
- Use short-lived branches named `feature/`, `fix/`, `docs/`, or `chore/`
  followed by a short description, and integrate them through pull requests.
- Keep pull requests small enough to review in one pass. Prefer rebasing onto
  `origin/main` over merging `main` into the branch, and delete merged branches.
- Incomplete behaviour stays off `main`. Prefer splitting work into complete
  vertical slices over merging dormant code behind a flag.

## Change Discipline

- Prefer small, reviewable changes with focused tests.
- Keep docs, tests, and code changes together when they describe the same behavior.
- Do not duplicate technical behavior in prose when code or tests are the source of truth.
- Do not rewrite unrelated files or reformat broad areas unless that is the task.
- Preserve user changes already present in the working tree.

## Verification

- Run the narrowest useful verification first, then broaden it when a change touches shared behavior.
- Record commands run and any skipped verification in the final response.
- If CI has run for the change, check it with `gh` before considering the work complete when credentials and network access are available.

## Domain And Safety Notes

- Keep provider-specific blockchain observation behavior isolated from the common payment lifecycle.
- Keep payment detection, settlement, webhook delivery, and audit behavior explainable.
- Do not introduce key custody unless a future ADR explicitly changes the non-custodial product boundary.
- Treat payment state transitions, underpayment settlement, webhook credentials, bearer tokens, and admin MCP actions as security- and audit-relevant.
- Logs go to a logaffe installation through `Logaffe.Extensions.Logging`, additive to the console provider ([ADR 0025](docs/adr/0025-logs-are-delivered-to-logaffe.md)). Traces and metrics follow OpenTelemetry/OTLP, with Grafana Alloy as the preferred collector and the Grafana LGTM Stack behind it. There is no separate error tracker: an error is an entry ([ADR 0026](docs/adr/0026-an-error-is-an-entry-and-there-is-no-error-tracker.md)).

## Architecture Baselines

Each area below has one baseline document that states what the implementation
must do, and one or more ADRs in [docs/adr/](docs/adr/) that explain why. When
they disagree, the ADR is the decision and the baseline is out of date. New
work follows the baseline; work that needs to depart from it records an ADR
first.

- Auth and admin security: [docs/architecture/admin-security-baseline.md](docs/architecture/admin-security-baseline.md).
  Local Admin Accounts, TOTP, step-up, CSRF, and one Admin permission level are
  accepted decisions. Do not introduce an external identity provider or a
  product-specific OAuth/OIDC mechanism without an ADR.
- Integration API: [docs/architecture/integration-api-contract.md](docs/architecture/integration-api-contract.md).
  Everything under `/api/v1/` is public contract. Versioning, `ProblemDetails`,
  validation shape, OpenAPI generation, and the contract diff are binding.
- Webhooks: [docs/architecture/webhook-event-contract.md](docs/architecture/webhook-event-contract.md).
  Signed, versioned, at-least-once, and snapshotted.
- Frontend: [docs/architecture/frontend-baseline.md](docs/architecture/frontend-baseline.md).
  Protected mutations, authorization, persistence, and provider commands stay
  backend-owned; product-specific payer and admin UI decisions are local.
- Persistence: [docs/architecture/database-conventions-baseline.md](docs/architecture/database-conventions-baseline.md).
  Naming, schema boundaries, ID types, time values, technical columns, and
  controlled migration execution.
- Background work: [docs/architecture/background-jobs-outbox-scheduler-baseline.md](docs/architecture/background-jobs-outbox-scheduler-baseline.md).
  Durable jobs, outbox, scheduler, leases, and retry behaviour.
- Admin MCP: [docs/architecture/admin-mcp-contract.md](docs/architecture/admin-mcp-contract.md).
  The tool list is a security boundary and is narrower than the Admin UI.
- Secrets and configuration: [docs/architecture/secrets-configuration-baseline.md](docs/architecture/secrets-configuration-baseline.md).
- Deployment and operations: [docs/architecture/deployment-operations-baseline.md](docs/architecture/deployment-operations-baseline.md).
- Observability: [docs/architecture/observability-baseline.md](docs/architecture/observability-baseline.md).

## Testing Expectations

- Payment state transitions, Integration API contracts, admin MCP actions,
  webhook delivery, security boundaries, and persistence behaviour are the
  high-risk areas. Test scope and local verification broaden accordingly.
- Contract snapshots under `docs/contracts/` are regenerated and diffed rather
  than edited by hand. A snapshot change that was not intended is a failure,
  not a formality.
- A change is done when the affected tests pass locally and the reasoning for
  anything left unverified is stated in the final response.
