# MCP Is Narrower Than the Admin UI, and Stays Local

The `admin` MCP surface is reachable over `stdio` by an agent CLI running beside
the installation. It is not exposed over HTTP, and it deliberately cannot do
everything an admin can.

An agent gets six reads — `payment.search`, `payment.inspect`,
`configuration.summarize`, `webhook_delivery.search`,
`address_pool.summarize`, `audit_log.search` — and three writes:
`payment.settle`, `webhook_delivery.resend`, and
`address_pool.import_native_eth`. What it does not get is credential management,
webhook endpoint configuration, or system configuration. Those are the actions
that change who can talk to the installation and how it decides payments are
paid, and they belong to a person at a browser who was asked to confirm.

Mirroring the Admin UI was the alternative and would have been less code. It was
rejected because [ADR 0018](./0018-every-admin-is-fully-privileged.md) removed
roles: there is no permission level that makes an agent less dangerous than the
admin whose session it is acting under, so the narrowing has to live in the tool
list. The tool list is therefore a security boundary, and it is snapshotted at
`docs/contracts/mcp/admin.snapshot.json` and diffed in CI so that widening it is
a reviewed act rather than a merge.

Tool names are domain-shaped rather than CRUD-shaped, because `payment.settle`
tells an agent what it is about to do and `payments.update` does not.

## Consequences

**Remote MCP is deferred, not designed.** Exposing this over HTTP needs an
authorization story that local `stdio` does not, and inventing a
product-specific one would be the wrong way to get it. Until there is a
deliberate answer, the transport stays local.

**Every write is audited as the admin who owns the session.**
`PAYAFFE_MCP_ADMIN_ACCOUNT_ID` names that account, so an agent action is
attributable to a person, and the risky ones carry the same confirmation and
step-up expectations as their UI equivalents
([ADR 0019](./0019-the-second-factor-is-totp-and-sensitive-writes-need-a-fresh-one.md)).

**Results are structured and errors are coded.** Tools return structured data
with stable machine-readable error codes, safe messages, and correlation ids;
internal diagnostics and provider payloads do not cross this boundary.

**The surface is rate-limited.** `PAYAFFE_MCP_PERMIT_LIMIT` and
`PAYAFFE_MCP_WINDOW` bound an agent that loops, which is a failure mode agents
have and humans mostly do not.

The full contract is in
[admin-mcp-contract.md](../architecture/admin-mcp-contract.md).
