# Frontend Baseline

This document records the target frontend baseline for `payaffe`.

## Scope

The MVP frontend includes:

- Payer Page,
- Admin UI,
- session-near UI states,
- forms,
- API consumption,
- frontend error handling,
- frontend tests.

The frontend does not own:

- payment lifecycle rules,
- protected mutations,
- authorization decisions,
- persistence,
- blockchain provider commands,
- webhook delivery commands,
- Audit Log writes,
- admin MCP behavior.

## App Boundary

The first frontend implementation uses one Next.js App Router application under
`apps/web`.

The app serves Payer and Admin routes. Route structure should make the two
surfaces clear, but separate apps are not required for the MVP.

Next.js Server Components may be used for rendering, layout, metadata, and
safe read paths. They must not contain domain logic or protected mutation
logic.

Next.js Route Handlers are not the public Integration API. If a BFF layer is
introduced later, it needs a product-specific architecture decision.

## Tech Stack

The frontend stack is:

- React with TypeScript,
- Next.js App Router,
- Node.js 24,
- pnpm through Corepack,
- TanStack Query,
- Tailwind CSS,
- shadcn/ui,
- React Hook Form,
- Zod,
- next-intl,
- Vitest,
- React Testing Library,
- Playwright,
- axe,
- MSW.

No frontend toolchain files are created before implementation starts.

## API Consumption

Frontend API access uses generated clients from backend contracts once those
contracts exist.

The client layer handles:

- `ProblemDetails`,
- validation error mapping,
- `correlationId`,
- authentication and authorization UI states,
- CSRF header attachment for unsafe Admin browser requests,
- pagination, sorting, and filtering parameters where applicable,
- `ETag` and `If-Match` if a frontend mutation uses concurrency-controlled
  resources.

Components should receive UI-near states instead of parsing raw API errors in
many places.

## Server State And Polling

TanStack Query manages server state.

Expected server-state flows include:

- Payer Page Payment status polling,
- Payment Option availability,
- Admin Payment search and inspection,
- Webhook Delivery history,
- Address Pool status,
- Observation Health,
- Audit Log search,
- configuration summary.

Polling intervals and refetch behavior should respect payment status,
Observation Health, page visibility, and backend rate-limit expectations.

React local state is for component-local UI state, not API caches.

## Forms And Validation

Forms use React Hook Form for form state.

Zod may provide UI-near validation for:

- Payment creation integration examples or admin test forms if added,
- Admin login and MFA forms,
- Webhook Endpoint forms,
- Integration API Credential forms,
- Address Pool CSV import,
- configuration forms.

Backend validation remains authoritative. API validation errors from
`ProblemDetails.errors` are mapped to fields and localized in the UI.

## Admin Security UI

Admin UI unsafe browser mutations must include `X-CSRF-TOKEN`.

The Admin UI must represent these states without exposing sensitive details:

- unauthenticated,
- MFA required,
- step-up required,
- session expired,
- authorization denied,
- CSRF failure,
- rate-limited or locked out,
- unexpected error with `correlationId`.

Frontend route guards are UX only. Backend authorization remains authoritative.

## Browser Storage

Browser-readable storage must not contain:

- Integration API bearer tokens,
- session secrets,
- CSRF secrets,
- MFA secrets,
- recovery codes,
- webhook secrets,
- provider API keys,
- private keys,
- seed phrases,
- sensitive payment payloads,
- arbitrary Payment Context Field dumps.

Allowed browser storage is limited to non-sensitive UI preferences, such as:

- theme,
- table density,
- visible columns,
- last non-sensitive Admin view,
- non-sensitive filters.

TanStack Query cache persistence is not part of the MVP baseline. It requires a
separate decision if API data would be persisted in browser storage.

Logout clears product-related client caches and non-essential session-near UI
state.

## Routing And URL State

Routes should reflect stable UI views and resources, not database internals.

Search, filter, sort, and pagination state may be represented in query
parameters when shareable or restorable.

URLs must not contain secrets, tokens, personal free text, raw provider
payloads, or sensitive payment context values.

Payer Page URLs expose a Payment identifier suitable for payer access. Admin
routes still require Admin authentication and backend authorization.

## Internationalization

The frontend uses next-intl.

UI text uses message keys. API error codes and validation codes are translated
into user-facing text in the frontend.

Dates, times, fiat amounts, cryptocurrency amounts, and relative times are
formatted with locale-aware utilities.

## Accessibility

The frontend uses semantic HTML first and shadcn/ui or Radix primitives for
complex controls.

Required baseline:

- keyboard-accessible controls,
- visible focus states,
- associated labels, descriptions, and errors for form fields,
- accessible dialogs, menus, popovers, tabs, and comboboxes,
- automated axe checks for central flows,
- manual keyboard review for central Payer and Admin flows.

## Frontend Observability

Unexpected client-side errors are reported to the product's own API and logged there, rather than to a third-party
target when configured.

Reports include release or version, deployment environment, and correlation
identifier where available.

Reports must not include tokens, secrets, session contents, full payment
payloads, provider raw data, private keys, seed phrases, or personal free text.

Analytics or performance tracking beyond technical error reporting requires a
separate privacy and sampling decision.

## Tests

Implementation must include focused frontend tests for:

- Payer Page status and Payment Option states,
- payment currency selection UI behavior,
- Admin login, MFA, and session states,
- CSRF-required Admin mutation handling,
- form validation and API validation error mapping,
- `ProblemDetails` error rendering with `correlationId`,
- authorization denied and not-found states,
- Address Pool import validation,
- Webhook Delivery resend flow,
- Audit Log search access,
- accessibility with axe,
- central Payer and Admin Playwright flows,
- generated API client integration with MSW-backed tests.
