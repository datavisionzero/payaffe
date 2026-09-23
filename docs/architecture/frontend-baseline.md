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

One React and TypeScript single-page application under `apps/web` serves the
Payer Page and Admin UI. Vite builds it, React Router owns browser routes, and
the ASP.NET Core API host serves the production assets. There is no frontend
Node.js runtime and no frontend-owned backend or BFF.

Payer and Admin routes remain visibly separate, but they share the application,
token layer, generated API client, error handling, and build.

The Admin surface is a route per view, framed by one shell that holds the
section navigation, signed-in identity, and an always-visible Selected Project
control. Project-owned deep links carry `projectId` in the route; browser-local
selection is only a convenience and is never authorization evidence:

| Route | View |
| --- | --- |
| `/admin/login` | Sign-in and the second step, outside the shell |
| `/admin` | Installation overview and redirect into a Selected Project when appropriate |
| `/admin/projects` | Project list, creation, status, and archival |
| `/admin/projects/{projectId}` | Project overview: Reorg Alerts, Webhook Delivery backlog, Address Pool capacity, and recent Payments |
| `/admin/projects/{projectId}/payments` | Payment list |
| `/admin/projects/{projectId}/payments/{paymentId}` | Payment detail and manual Settlement |
| `/admin/projects/{projectId}/monitoring` | Project payment health and Reorg Alerts, with installation-wide Observation Health labelled separately |
| `/admin/projects/{projectId}/webhooks` | Webhook Endpoints, and Webhook Deliveries under `?view=deliveries` |
| `/admin/projects/{projectId}/integrations` | Integration API Credentials |
| `/admin/projects/{projectId}/addresses` | Project Watch-Only Wallet Sources and native ETH Address Pool |
| `/admin/audit-log` | Audit Log list and export |
| `/admin/audit-log/{eventId}` | Audit Log entry detail |
| `/admin/account` | Session, step-up, recovery codes, sign-out |

The navigation carries a count for the sections that can need attention, so an
unresolved Reorg Alert, a Webhook Delivery backlog, or a draining Address Pool
is visible from any Admin route rather than only from the page that lists it.
Counts and links are scoped to the Selected Project unless explicitly labelled
as installation-wide.

The shell renders before route data, remains mounted during navigation, and
provides explicit busy, empty, unreachable, not-found, and session-expired
states. Route-level code splitting is allowed for heavier detail and mutation
screens; it must not turn navigation into a blank page.

## Reference Revisions

The accepted frontend references are:

- `datavisionzero/hostingaffe` revision
  `97a58753ea86861aa6611ca75853e24e6b69e050`;
- `datavisionzero/planaffe` revision
  `e816afa62d4b92f669601f57d0c8b3b6f2dc91f7`.

They are references, not runtime or package dependencies. Payaffe may copy or
adapt the token layer, Theme Provider, repository-owned Base UI components,
responsive Sidebar, shell patterns, Project switcher, loading states, and Vite
hosting pattern. It must replace their domain routes, API types, text, storage
keys, and product names with Payaffe concepts. Later upstream drift does not
change this baseline implicitly.

Both repositories are MIT licensed with the same 2026 `datavisionzero`
copyright notice as Payaffe. The root license must remain present in source and
binary distributions. If code is later taken from a differently licensed
source, its required notice is added before that code lands.

## Static Hosting And Fallback

The production API image builds `apps/web` in a Node.js 24 plus Corepack/pnpm
stage and copies the Vite output into the ASP.NET Core static-file root. The
runtime image contains the .NET application and static assets, not Node.js.

Routing precedence is:

1. `/api/**` and `/health/**` are backend paths and never receive SPA fallback;
2. existing hashed assets and other static files are served with their correct
   content type; immutable hashed assets receive long-lived cache headers while
   `index.html` does not;
3. an unmatched `GET` or `HEAD` document navigation that accepts `text/html`
   and has no file extension receives `index.html`;
4. unknown API paths, non-document requests, and missing asset paths keep their
   backend or static-file `404` response.

Vite development proxies `/api` and `/health` to the local API while retaining
HTML navigations for the SPA. The browser uses relative URLs in development and
production. No API origin is compiled into the bundle.

## Route And Workflow Parity

The migration changes the router and Project shape, not the capabilities that
already exist. Route acceptance is:

| Current route | Target route | Required workflow |
| --- | --- | --- |
| `/` | `/` | Explain that Payer Pages use payment-specific links and link to Admin sign-in without exposing sample payment data. |
| `/pay/{payerPageId}` | unchanged | Load Payment, show Payment Options, select currency, show amount/address/server-authored QR instruction/expiration, poll status, and show Return URL when eligible. |
| `/admin/login` | unchanged | Password start, optional MFA completion, generic failures, lockout/rate-limit state, and return to intended Admin route. |
| `/admin` | unchanged | Show installation summary and choose or redirect to a Project without silently inventing Project context. |
| none | `/admin/projects` | List, create, disable, re-enable, archive, and restore Projects according to lifecycle rules. |
| `/admin` | `/admin/projects/{projectId}` | Project overview with recent Payments and attention summaries. |
| `/admin/payments` | `/admin/projects/{projectId}/payments` | Search and filter Project Payments with URL-owned filter state. |
| `/admin/payments/{paymentId}` | `/admin/projects/{projectId}/payments/{paymentId}` | Inspect evidence and histories and perform manual Settlement with concurrency, step-up, and confirmation. |
| `/admin/monitoring` | `/admin/projects/{projectId}/monitoring` | Show Project Reorg Alerts and payment health; label installation-wide Observation Health distinctly. |
| `/admin/webhooks` | `/admin/projects/{projectId}/webhooks` | Manage endpoints, switch to Delivery history, inspect failures, and manually resend with confirmation. |
| `/admin/integrations` | `/admin/projects/{projectId}/integrations` | Create, rotate, and disable Project Integration API Credentials while showing bearer tokens once. |
| `/admin/addresses` | `/admin/projects/{projectId}/addresses` | Show BTC/LTC source status, native ETH capacity and imports, and Project-scoped warnings. |
| `/admin/audit-log` | unchanged | Search and export installation-wide Audit Log entries with an explicit Project filter. |
| `/admin/audit-log/{eventId}` | unchanged | Inspect one security-relevant event and its Project context when present. |
| `/admin/account` | unchanged | Show session, MFA/step-up and recovery state, and sign out. |

The existing Admin session-expiry behavior, CSRF acquisition and retry rules,
one-time secret display, optimistic concurrency, `ProblemDetails` mapping,
client-error reporting, attention counts, and logout cache clearing retain
parity. A route is not migrated until a reload and direct deep link behave the
same as client-side navigation.

Legacy Admin URLs may redirect only when the default Project is unambiguous.
They must not select an arbitrary Project in a multi-Project installation.

## Tech Stack

The frontend stack is:

- React with TypeScript,
- Vite,
- React Router,
- Node.js 24,
- pnpm through Corepack,
- TanStack Query,
- Tailwind CSS,
- Base UI through repository-owned shadcn components,
- Lucide icons,
- React Hook Form,
- Zod,
- Vitest,
- React Testing Library,
- Playwright,
- axe,
- MSW.

The generated OpenAPI layer continues to use `openapi-typescript` and
`openapi-fetch`. The root pnpm workspace and lockfile remain authoritative; the
npm lockfiles in the reference repositories are not copied.

## Visual Acceptance

The application adopts the references' visual language, not their product
screens.

### Tokens And Type

- IBM Plex Sans is the UI and heading family at weights 400, 500, and 600.
- IBM Plex Mono is used for addresses, identifiers, hashes, amounts where
  alignment matters, and keyboard hints at weights 400 and 500.
- Fonts are bundled through `@fontsource`; production rendering does not depend
  on a third-party font CDN.
- The token layer removes Tailwind's default palette. Product components use
  named semantic variables rather than raw palette classes or arbitrary color
  values.
- Light neutrals start from reference values `#f7f7f5` background,
  `#1d1f1c` foreground, `#ffffff` card, `#eef0eb` muted surface,
  `#6b6f68` muted text, and `#e4e5e1` border.
- Dark neutrals start from `#161816` background, `#e8e9e4` foreground,
  `#1e211e` card, `#262a26` muted surface, `#9a9e96` muted text, and
  `#2c302c` border.
- The brand accent is teal: `oklch(0.62 0.11 190)` in light mode and
  `oklch(0.7 0.1 190)` in dark mode, with `#e6f1ef` and `#243230` as soft
  brand surfaces. Destructive and payment-status colors are separate semantic
  tokens and are never the only carrier of meaning.
- The base radius is `0.375rem`. Controls, panels, tables, and dialogs use the
  shared radius scale rather than screen-specific rounding.

Exact reference values are the starting token contract. A deliberate Payaffe
change updates this section and its visual fixtures; incidental drift from
copied shadcn defaults is a failure.

### Shell And Density

- The signed-in Admin shell has a persistent left navigation, a 3rem top bar,
  an always-visible Project switcher, and an account menu at the right.
- The Project switcher reads Project context from the URL, names the current
  Project, links to the Project overview, and preserves the equivalent view
  when switching where that view exists.
- Navigation groups separate operations, configuration, and installation
  security. Attention badges have accessible names and are scoped to the
  Selected Project unless visibly labelled otherwise.
- Screen headers use a compact 3rem minimum row with a small semibold title,
  muted metadata, and actions aligned consistently. Body text defaults to the
  compact `text-sm` scale.
- Lists and tables favor scan density: one primary line per item, bounded
  secondary text, aligned numeric columns, and stable row actions. Dense never
  means unlabeled, clipped without disclosure, or keyboard-inaccessible.
- The shell renders before data and never becomes a blank canvas. Loading,
  empty, failed, and stale-link states are named in text and announced where
  appropriate.
- A command palette or shortcut control is added only with useful Payaffe
  commands behind it; the migration does not copy an inert reference
  affordance.

### Theme And Mobile

- Theme choices are light, dark, and system, stored only under
  `payaffe.theme`. The resolved class is applied to `<html>` before first paint
  so a saved dark theme does not flash light.
- Both themes meet WCAG AA contrast for normal text and visible focus; status,
  warning, observed, completed, expired, and destructive states also include
  text or icon semantics.
- Below the desktop breakpoint the same navigation becomes an off-canvas
  drawer. The Project switcher, account access, attention, and every supported
  action remain available; mobile is not a reduced product.
- Payer Pages do not inherit the Admin sidebar. They use the same tokens and
  typography in a single-column, touch-friendly layout that keeps amount,
  Payment Address, QR code, expiration, status, and copy actions readable at
  320 CSS pixels without page-level horizontal scrolling.
- Data regions that genuinely need horizontal space use a labelled local
  scroller. Dialogs and sheets stay within the viewport and restore focus on
  close.

Acceptance includes reviewed light and dark screenshots at desktop and mobile
widths for sign-in, Project overview, Payment list and detail, address supply,
and each major Payer Page state. Screenshot review guards hierarchy, density,
overflow, and theme coverage; it is not a pixel-exact cross-platform test.

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

After Currency Selection the Payer Page uses the returned Payment
Instruction's `uri` unchanged for its wallet link and QR payload. Frontend code
does not reconstruct BTC, LTC, or native ETH URIs from display fields.

In a Test Mode installation
([ADR 0033](../adr/0033-a-test-installation-simulates-its-external-truth.md))
the Payer Page and the Admin UI show a persistent test-mode indicator, read from
the Payment's `testMode` and the Admin session's `testMode`. While a test
Payment waits for its money, the Payer Page offers to simulate paying the exact
amount, half of it, or half again; the backend owns the simulation through a
Payer route that only a Test Mode installation maps. The browser API snapshot
under `docs/contracts/web/` is taken from a Test Mode installation so the web
application is typed for that route; a live installation serves the same
document without it.

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
- non-sensitive filters,
- last Selected Project identifier.

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

## Language And Formatting

The MVP interface is English. `next-intl` and its Next.js request integration
are removed during the migration; strings live with the screen or shared
component that owns them. Stable API error and validation codes map to
consistent English text in one frontend error module rather than being rendered
raw.

Dates, times, fiat amounts, cryptocurrency amounts, and relative times use the
browser `Intl` APIs through shared formatting functions. Formatting must not
change stored values, signatures, copied Payment Addresses, or the exact amount
a Payer must send.

A second language with a real audience reopens this decision and introduces a
catalog and locale routing deliberately. Placeholder localization machinery is
not retained in the meantime.

## Accessibility

The frontend uses semantic HTML first and repository-owned shadcn/Base UI
primitives for complex controls.

Required baseline:

- keyboard-accessible controls,
- visible focus states,
- associated labels, descriptions, and errors for form fields,
- accessible dialogs, menus, popovers, tabs, and comboboxes,
- automated axe checks for central flows,
- manual keyboard review for central Payer and Admin flows.

## Frontend Observability

Unexpected client-side errors are reported to the product's own API and logged
there, rather than to a third-party target.

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
- server-authored BTC, LTC, and native ETH Payment Instruction links and QR
  payloads,
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
- generated API client integration with MSW-backed tests,
- React Router deep links, reloads, unknown routes, and legacy-route handling,
- production SPA fallback exclusions for `/api`, `/health`, and missing assets,
- light/dark/system theme initialization without a wrong-theme first paint,
- desktop and mobile overflow and visual-review fixtures,
- Project switching preserving route context without leaking cached Project
  data,
- static asset caching that keeps `index.html` revalidatable and hashed assets
  immutable.
