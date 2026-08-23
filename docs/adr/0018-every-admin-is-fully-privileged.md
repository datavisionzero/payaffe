# Every Admin Is Fully Privileged

There are no roles. An Admin Account can settle a payment, create and revoke
integration credentials, change webhook endpoint secrets, import ETH addresses,
change the observation mode, and read and export the audit log. All of them, or
none of them by not being an admin.

Roles were rejected for the MVP because a role model that nobody needs is worse
than no role model at all: it produces a permission matrix that is maintained
speculatively, checked inconsistently, and eventually worked around by giving
everyone the role that works. One installation serves one shop
([ADR 0017](./0017-one-installation-serves-one-shop.md)), and the admins of a
single shop are a small group of people who already trust each other with the
bank account.

What replaces roles is not weaker authorization but a different axis of it.
Every protected action is authorized server-side and fails closed when
authentication, session validity, MFA state, step-up state, or actor identity is
missing or invalid — and the sensitive subset additionally requires a fresh
second factor, which is
[ADR 0019](./0019-the-second-factor-is-totp-and-sensitive-writes-need-a-fresh-one.md).

## Consequences

**The audit log carries the weight roles would have.** Since permission cannot
distinguish who may do a thing, the record of who did it is what remains. Audit
entries are append-only, kept at least 180 days, and cover the actions with
payment or security impact.

**"Admin" is a meaningful threshold.** Creating one is a deliberate act, and
there is no lesser account to hand out to someone who only needs to look at
payments. That is a real limitation for a shop with a support employee, and it
is the situation that would justify reopening this.

**MCP is narrower than the admin, not narrower than a role.** The reduced
surface an agent gets is a property of the MCP contract
([ADR 0023](./0023-mcp-is-narrower-than-the-admin-ui-and-stays-local.md)), not of
a permission level — which is why that narrowing has to be enforced by the tool
list rather than by authorization.

The complete authorization and session rules are in
[admin-security-baseline.md](../architecture/admin-security-baseline.md).
