# The Hosts Are Thin and the Core Is Shared

The backend is `Payaffe.Domain`, `Payaffe.Application`, and
`Payaffe.Infrastructure` under `src/`, with four deployable hosts over them
under `apps/`: `api`, `worker`, `mcp`, and `migrations`. Dependencies point
inward and the compiler holds them there. The obvious alternative is to let each
host own what it needs, and for four hosts that each do one job it is a real
one — the migrations host in particular barely needs a domain at all.

What decided it is that three of these hosts change payment state. The API
settles an underpaid payment when an admin says so, the worker completes one
when the confirmations arrive, and MCP settles one when an agent is asked to.
If each host owned its own copy of that rule, the three would drift, and the
drift would be discovered by whoever is reconciling a payment that two surfaces
disagree about. Sharing the core turns "these must agree" from a matter of
review into a matter of there being one implementation to disagree with.

## Consequences

**A host is an adapter and holds no domain logic.** `apps/` is allowed to know
about HTTP, MCP framing, hosted-service lifetimes, and configuration binding.
It is not allowed to know when a payment is complete. Where a host starts
growing a rule, that rule belongs in `src/` — this is the check that makes the
structure worth its cost, and it is checkable by reading.

**The frontend is not part of this.** `apps/web` is a Next.js application that
talks to the API over generated clients and owns none of the above; see
[ADR 0022](./0022-the-backend-owns-every-protected-mutation.md) for where that
boundary sits and why it is drawn so firmly.

**The price is paid on every feature.** A new payment field is a type in Domain,
a use case in Application, a mapping and a migration in Infrastructure, and a
contract change in one or more hosts. That is the recurring cost, it is the
strongest argument the per-host alternative had, and it is accepted because the
alternative's cost is paid in incidents rather than in keystrokes.

The folder contract this follows is recorded in
[repository-structure.md](../architecture/repository-structure.md).
