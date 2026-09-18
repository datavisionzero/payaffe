# The Web Application Is a Vite SPA Served by the API

The Payer Page and Admin UI are one React and TypeScript single-page
application built with Vite and routed with React Router. Production builds are
static assets served by the ASP.NET Core API host; Node.js is a build tool, not
a runtime service. This replaces the Next.js runtime topology described in
[ADR 0001](./0001-deployment-is-compose-and-the-service-count-is-the-budget.md),
[ADR 0002](./0002-the-hosts-are-thin-and-the-core-is-shared.md),
[ADR 0022](./0022-the-backend-owns-every-protected-mutation.md), and
[ADR 0024](./0024-the-browser-api-lives-under-api-on-one-origin.md) without
changing their shared-core, backend-mutation, generated-client, or same-origin
decisions.

The application follows the inspected frontend foundations of
`datavisionzero/hostingaffe` at
`97a58753ea86861aa6611ca75853e24e6b69e050` and
`datavisionzero/planaffe` at
`e816afa62d4b92f669601f57d0c8b3b6f2dc91f7`. Both use Vite, React Router,
Tailwind CSS v4, Base UI behind repository-owned shadcn components, generated
OpenAPI clients, IBM Plex, warm neutral tokens, a teal accent, and one
responsive shell. Planaffe also demonstrates the URL-owned Project switcher
Payaffe now needs.

Keeping Next.js was rejected because Payaffe uses no required server rendering,
Server Action, or frontend-owned backend. Its pages load and mutate through the
.NET API, so a permanent Node runtime and a second published service buy no
product capability. Sharing a package with either reference application was
also rejected: Payaffe adopts the proven foundation and selected components at
pinned revisions, then owns its copy and lets it diverge with its payment
domain.

## Consequences

**The API and application share one namespace.** `/api/**` and `/health/**`
always belong to ASP.NET Core. Eligible browser document navigations fall back
to `index.html`; unknown API paths and missing assets never do. Development
uses Vite's proxy and the same relative URLs.

**The runtime loses a service.** The API image has a Node 24 and pnpm build
stage, copies the Vite output into `wwwroot`, and runs only the .NET runtime.
The separate `web` image, container, port, cross-origin configuration, and
compiled public API address disappear.

**The frontend boundary remains strict.** Protected mutations still go through
the generated .NET API client with Admin session, CSRF, authorization, step-up,
Audit Log, and outbox enforcement. TanStack Query still owns server state;
React Hook Form and Zod still own form state and UI-near validation.

**The repository keeps its own toolchain choices.** Payaffe stays in the root
pnpm workspace through Corepack on Node.js 24 even though the reference
repositories use npm. `next-intl` is removed; the MVP interface is English and
uses browser `Intl` APIs for locale-aware value formatting. A real second
language is the trigger for choosing localization machinery.

**The reference is attributed, not coupled.** Both source repositories and
Payaffe are MIT licensed with the same 2026 `datavisionzero` copyright notice.
Substantial copied or adapted files retain that notice through the repository
license, and the pinned revisions remain recorded in the frontend baseline so
future changes can distinguish adopted source from later Payaffe work.

The route parity, visual acceptance, build, fallback, and test rules are in
[frontend-baseline.md](../architecture/frontend-baseline.md).
