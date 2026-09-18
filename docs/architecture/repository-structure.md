# Repository Structure

This document defines the high-level folder structure of this repository. It describes the target structure; folders are created only when they are needed.

## Principle

The repository follows a monorepo-capable structure. Deployable hosts, shared backend modules, frontend packages, tests, and documentation stay clearly separated so humans, CI, and agents can recognize project boundaries quickly.

## Target Structure

```text
.
├── apps/
├── src/
├── tests/
├── packages/
├── docs/
├── deploy/
├── scripts/
└── <root configuration files>
```

## Folder Meaning

`apps/` contains deployable or locally runnable applications and hosts. Typical examples are `apps/web` for a Vite frontend source package, `apps/api` for an ASP.NET Core HTTP API that may serve the built frontend, `apps/auth` for an auth host, `apps/mcp` for an MCP host, `apps/worker` for background processing, or `apps/migrations` for a migration runner. Not every repository needs every host.

`src/` contains shared backend code and core modules, especially Domain,
Application, Infrastructure, and comparable libraries. The independently
packaged `Payaffe.Sdk` also belongs here because it is a .NET library rather
than a deployable host. Deployable hosts and the SDK must not duplicate domain
logic; they use the public application or HTTP boundaries respectively.

`tests/` contains automated tests that do not fit naturally next to one package or host. Backend test projects, integration tests, and cross-cutting contract tests belong here.

`packages/` is optional and reserved for shared frontend or TypeScript packages, such as UI building blocks, API clients, or shared validation logic. It is created only when more than one app or a clear reuse case exists.

`docs/` contains durable product, architecture, operations, and process documentation. Standard subfolders are `docs/product/`, `docs/architecture/`, `docs/adr/`, `docs/contracts/`, and, when needed, `docs/operations/`.

`deploy/` is optional and reserved for deployment artifacts that outgrow simple root files, such as production Compose files, reverse-proxy configuration, server bootstrap, migration-runner configuration, or observability provisioning such as dashboards and alert rules. A simple root-level `docker-compose.yml` remains allowed for local development.

`scripts/` is optional and reserved for reusable development, CI, migration, or operations helpers. One-off local commands do not automatically belong here.

Root configuration files stay in the repository root when tools expect them there or when visibility is helpful. Examples include solution files, `global.json`, `package.json`, `pnpm-workspace.yaml`, lockfiles, `.nvmrc`, `.gitignore`, `README.md`, `AGENTS.md`, `LICENSE`, `SECURITY.md`, and local Compose baselines. CI definitions live under `.github/workflows/`.

## Rules

- Missing folders are not created in advance.
- New top-level folders need a clear purpose and should not duplicate this structure.
- Domain logic should not live permanently in deployable hosts when it should be shared through `src/`.
- The public .NET SDK belongs in `src/Payaffe.Sdk` with focused tests under
  `tests/Payaffe.Sdk.Tests`; those folders are created only with the SDK
  implementation.
- Frontend code belongs under `apps/web` or, for reusable building blocks, under `packages/`.
- Documentation should respect existing document types and avoid duplicating decisions.
- Deviations are allowed, but they should be justified in an ADR or in the architecture documentation.
