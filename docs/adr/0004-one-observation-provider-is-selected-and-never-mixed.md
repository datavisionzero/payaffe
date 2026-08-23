# One Observation Provider Is Selected, and Never Mixed

An installation runs in exactly one Blockchain Observation Mode, chosen by the
admin. The initial modes are Blockchair sparse polling — polling only the
addresses of payments actually expecting a deposit — and NOWNodes as a hosted
node provider. Neither is a default the product picks on the operator's behalf,
because the two differ in ways an operator has standing to care about:
Blockchair is long-established and independent, NOWNodes is cheaper per request
and accepts cryptocurrency, and NOWNodes is commercially related to NOWPayments.

The tempting alternative is mixing them — one provider for detection, another
for confirmation, or automatic failover when one is down. It is refused because
[ADR 0003](./0003-blockchain-truth-comes-from-a-hosted-api-not-a-node-we-run.md)
made the provider the arbiter of whether money arrived. Two arbiters means that
when they disagree there is a third rule deciding which one wins, and that rule
is the one nobody writes down and everybody discovers during an incident. One
selected mode keeps the answer to "why does this payment say paid" a single
name.

Block scanning was considered as the uniform strategy and rejected on Ethereum:
the chain produces too many blocks and the useful per-block endpoint costs too
much per call, which is exactly the cost the sparse-polling mode avoids by
looking only at addresses that are waiting for something.

## Consequences

**Provider behaviour is isolated behind an observation abstraction.** Adding a
provider is a new adapter and a new mode value, not a change to the payment
lifecycle or the Integration API. This is the part of the decision that has to
hold for the rest of it to be affordable.

**Failover is a manual act.** When the selected provider is down, Observation
Health reports it, affected currencies stop being offered, and the operator
changes the mode if they want to keep taking payments in them. That is worse
than automatic failover on a bad day and better than it on every day when the
question is which provider to believe.

**Free tiers are permitted and documented as a risk.** An operator may run on
one; the documentation says plainly that free tiers throttle, disappear, and
change terms, and that a paid plan is what detection reliability actually costs.

A cross-check or fallback mode remains possible, and it needs its own ADR
stating which provider wins a disagreement before any code is written.
