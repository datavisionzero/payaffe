# Admin MCP Host

The Admin MCP host is a local `stdio` process for agent-assisted operations by
an Admin. It is deliberately narrower than the Admin UI: it reads operational
data and performs manual Settlement, failed Webhook Delivery resend, and native
ETH Address Pool import. It cannot manage Integration API Credentials, Webhook
Endpoints, or system configuration.

The accepted tool contract is [ADR 0023](../adr/0023-mcp-is-narrower-than-the-admin-ui-and-stays-local.md)
and [admin-mcp-contract.md](../architecture/admin-mcp-contract.md). The
generated snapshot is `docs/contracts/mcp/admin.snapshot.json` and is compared
on every test run.

## Identity And Audit Attribution

The host acts as one configured Admin Account. `Mcp:Admin:AdminAccountId` must
name an existing, active account in `auth.admin_accounts`; startup fails when it
is empty, unknown, or disabled. A database that cannot be reached at startup is
not a refusal, because every tool call checks the account again before it acts:
once the account is disabled or removed, reads and writes alike are rejected
with `authentication.required`, and the refusal is audited with reason
`admin_account.disabled` or `admin_account.not_found`. Native ETH Address Pool
imports reference that account as the importing Admin.

Audit Log entries record the acting account as usual and set the source service
to `mcp`, which is what distinguishes MCP activity from Admin API activity.
Filter the Audit Log by `source_service = 'mcp'` to review everything the MCP
surface did.

Manual Settlement and Address Pool import write their Audit Log entry inside the
same transaction as the change, so the host records only the outcomes that never
reach that transaction, such as a missing confirmation, an exhausted rate limit,
or an unknown Payment. Webhook Delivery resend has no transactional audit, so
the host records both its success and its failure.

## Configuration

| Setting | Environment variable | Default |
| --- | --- | --- |
| `ConnectionStrings:Payaffe` | `ConnectionStrings__Payaffe` | required |
| `Mcp:Admin:AdminAccountId` | `Mcp__Admin__AdminAccountId` | required |
| `Mcp:Admin:PermitLimit` | `Mcp__Admin__PermitLimit` | `60` |
| `Mcp:Admin:Window` | `Mcp__Admin__Window` | `00:01:00` |
| `Webhooks:Delivery:AllowedPrivateTargets` | `Webhooks__Delivery__AllowedPrivateTargets` | empty |

The permit limit and window bound write-tool calls per host process. A Webhook
Delivery resend is delivered from this host, so it needs the same private-target
allowlist as the worker when a receiver sits on an internal address
([ADR 0036](../adr/0036-webhook-delivery-reaches-only-public-addresses.md)). The host
never registers the scheduled background workers, so it takes no worker lease
and processes no batches.

## Running It

From a built checkout:

```sh
dotnet run --project apps/mcp
```

Or as a container, keeping stdin attached for the `stdio` transport:

```sh
docker run -i --rm \
  -e ConnectionStrings__Payaffe="Host=...;Database=payaffe;Username=...;Password=..." \
  -e Mcp__Admin__AdminAccountId="<admin account uuid>" \
  ghcr.io/datavisionzero/payaffe-mcp:0.5.0
```

An installation started from [deploy/compose.yaml](../../deploy/compose.yaml)
reaches its database over that project's network, so add
`--network payaffe_default` and use `db` as the host. Build the image locally
with `docker build -f apps/mcp/Dockerfile -t payaffe-mcp .` when running from a
checkout instead.

Point the MCP client at that command. Because the transport is `stdio`, nothing
may write to standard output: the host logs to standard error only.

Invalid configuration is reported on standard error and exits with code `78`
rather than leaving the process running.

## Risky Tools

`payment.settle`, `webhook_delivery.resend`, and `address_pool.import_native_eth`
require an explicit `confirmed` input. Without it the call is rejected with
`confirmation.required` and the denial is audited. Every rejection carries a
machine-readable code and a correlation identifier; no stacktraces, SQL details,
tokens, secrets, or key material are returned.

## Updating The Contract Snapshot

Regenerate after an intentional tool change, then review the diff:

```sh
PAYAFFE_UPDATE_CONTRACT_SNAPSHOTS=true dotnet test tests/Payaffe.Mcp.Tests
```
