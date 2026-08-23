# Admin MCP Contract

This document records the target contract baseline for the `payaffe` admin MCP
surface.

## Scope

The admin MCP surface is product-local and narrower than the Admin UI.

It provides read access for:

- Payments and statuses,
- configuration summary,
- Webhook Delivery history,
- Address Pool status,
- Audit Log.

It provides write access for:

- manual Settlement of a Payment,
- resending failed Webhook Deliveries,
- importing native ETH Address Pool entries.

It does not create Integration API Credentials, change Webhook Endpoints,
change system configuration, perform refunds, sweep funds, withdraw funds, or
store spending keys.

## Transport And Auth Boundary

The MVP admin MCP surface is intended for local agent CLI usage by an Admin.
Local `stdio` access is allowed for local operator and development workflows.

Remote MCP over HTTP is not part of the local-Admin MVP baseline. If remote MCP
is added later, it must:

- be introduced by an ADR that decides its authorization path,
- expose remote MCP as a protected resource with its own audience,
- require an explicit access scope,
- provide required MCP Protected Resource Metadata,
- follow the shared CLI/native/device and service-principal standards where
  applicable.

Admin MCP actions are still product actions. The server-side implementation
must enforce Admin authentication, authorization, MFA, step-up where required,
and audit rules from the Admin security baseline.

## Surface And Snapshot

The stable MCP surface name is:

```text
admin
```

Once the MCP host exists, the accepted generated snapshot path is:

```text
docs/contracts/mcp/admin.snapshot.json
```

The snapshot folder is created only when stable MCP tools and generation
tooling exist.

## Tool Catalog

Initial read tools:

| Tool | Purpose |
| --- | --- |
| `payment.search` | Search or list Payments for operational review. |
| `payment.inspect` | Inspect one Payment, including status, evidence, and relevant histories. |
| `configuration.summarize` | Return a safe configuration summary without secrets. |
| `webhook_delivery.search` | Search Webhook Delivery history and failed deliveries. |
| `address_pool.summarize` | Return native ETH Address Pool capacity and status. |
| `audit_log.search` | Search security-relevant Audit Log entries. |

Initial write tools:

| Tool | Purpose | Confirmation |
| --- | --- | --- |
| `payment.settle` | Manually settle a Payment. | required |
| `webhook_delivery.resend` | Resend a failed Webhook Delivery. | required |
| `address_pool.import_native_eth` | Import native ETH Payment Addresses. | required |

Tool names are stable contracts. Breaking semantic changes require a new tool
name or a documented deprecation and replacement path.

## Tool Results

Tools return structured results.

Structured results should include:

- relevant resource IDs,
- status or outcome,
- safe summary text,
- correlation identifier,
- follow-up state when useful.

Human-readable summaries are allowed but are not the technical truth for
client follow-up.

## Errors

MCP tools use stable machine-readable error codes for validation, domain, and
authorization failures.

Initial error codes include:

- `validation.failed`,
- `authorization.denied`,
- `authentication.required`,
- `step_up.required`,
- `confirmation.required`,
- `payment.not_found`,
- `payment.not_settleable`,
- `webhook_delivery.not_found`,
- `webhook_delivery.not_resendable`,
- `address_pool.invalid_address`,
- `address_pool.duplicate_address`,
- `address_pool.import_empty`,
- `audit_log.access_denied`,
- `rate_limited`,
- `unexpected_error`.

Errors include a correlation identifier when possible.

MCP clients must not receive stacktraces, SQL details, tokens, secrets,
webhook secrets, MFA material, recovery codes, private keys, seed phrases, or
provider raw errors.

## Risk And Confirmation

Risky tools require explicit client-side confirmation before invocation:

- `payment.settle`,
- `webhook_delivery.resend`,
- `address_pool.import_native_eth`.

Risk reasons:

- `payment.settle` changes Payment resolution.
- `webhook_delivery.resend` triggers an external notification.
- `address_pool.import_native_eth` changes the future Payment Address supply.

Confirmation metadata must be visible in the generated MCP contract snapshot.

## Audit

Audit Log entries are required for:

- successful write tools,
- denied write tools,
- risky tool attempts when security- or payment-relevant,
- `audit_log.search`,
- sensitive or bulk read access when implementation adds such reads.

Audit Log entries record the acting Admin Account or MCP credential context,
subject, outcome, reason code, and correlation identifier without storing
request secrets or full sensitive payloads.

## Rate Limits And Abuse Protection

Admin MCP access requires rate limits or equivalent abuse protection.

Concrete limits are implementation and operations configuration, but they must
be documented before production use.

## Contract Checks

Once the MCP host exists, CI must:

- generate the admin MCP contract from the host,
- normalize the generated contract deterministically,
- compare it with `docs/contracts/mcp/admin.snapshot.json`,
- fail on unintended breaking changes,
- show review-required changes for tool descriptions, schemas, risk flags,
  confirmation, and error codes.

The snapshot is generated from the implementation. It is not manually maintained
as a second tool specification.

## Tests

Implementation must include focused tests for:

- schema validation for each tool,
- structured result shape,
- stable error codes,
- server-side authorization,
- step-up requirements for sensitive actions,
- confirmation metadata for risky tools,
- Audit Log entries for write and sensitive read tools,
- no secret raw values in results, errors, snapshots, logs, or Audit Log
  entries,
- snapshot generation and diff behavior.
