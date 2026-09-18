# Architecture

This directory contains architecture documentation for `payaffe`.

## Contents

- [technical-constraints.md](technical-constraints.md) records known constraints and preferred technologies.
- [observability-baseline.md](observability-baseline.md) records the observability target.
- [repository-structure.md](repository-structure.md) defines the high-level repository layout.
- [deployment-operations-baseline.md](deployment-operations-baseline.md) records the deployment, migration, health, backup, and restore baseline.
- [admin-security-baseline.md](admin-security-baseline.md) records the local Admin Account, session, CSRF, authorization, MFA, step-up, and Audit Log baseline.
- [secrets-configuration-baseline.md](secrets-configuration-baseline.md) records runtime configuration and secret-handling rules.
- [database-conventions-baseline.md](database-conventions-baseline.md) records PostgreSQL naming, schema, ID, timestamp, and migration rules.
- [project-isolation-baseline.md](project-isolation-baseline.md) records Project ownership, configuration, lifecycle, storage enforcement, and upgrade rules.
- [background-jobs-outbox-scheduler-baseline.md](background-jobs-outbox-scheduler-baseline.md) records durable job, outbox, scheduler, retry, and worker rules.
- [admin-mcp-contract.md](admin-mcp-contract.md) records the admin MCP tool contract baseline.
- [frontend-baseline.md](frontend-baseline.md) records the Payer Page and Admin UI frontend architecture baseline.
- [integration-api-contract.md](integration-api-contract.md) records the public Integration API contract baseline.
- [embedded-payment-sdk-baseline.md](embedded-payment-sdk-baseline.md) records the headless payment flow, target-product trust boundary, and .NET SDK baseline.
- [webhook-event-contract.md](webhook-event-contract.md) records the outgoing Webhook Event contract baseline.
- [blockchain-observation-options.md](blockchain-observation-options.md) tracks operator-controlled blockchain observation options.
- [hosted-blockchain-api-options.md](hosted-blockchain-api-options.md) evaluates hosted API options for Blockchain Truth.
- [../adr/](../adr/) contains the Architecture Decision Records these baselines follow from.
