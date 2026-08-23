# The Rate Locks When the Payer Picks a Currency

The exchange rate is captured at the moment the payer selects BTC, LTC, or
native ETH on the payer page — not when the shop created the payment, and not
when the coins arrive. That capture is the Rate Lock, and it holds until the
payment expires.

Both other moments were considered. Locking at creation is impossible to do
honestly: the payer has not chosen a currency yet, so there is nothing to lock a
rate *for*, and locking all three would mean holding three quotes that mostly go
unused. Locking at detection time is worse for the person actually paying —
they would be asked to send an amount that is not yet the amount, and any
movement between sending and confirming would become their problem or the
shop's. Selection is the one instant where the payer knows what they are being
asked for and the product can hold it fixed.

The source is CoinGecko, cached at an admin-configurable interval so that a
burst of payer traffic does not become a burst of API calls.

## Consequences

**A stale rate is preferable to no rate.** When CoinGecko is unreachable, a
cached rate may still be used until it exceeds the configured maximum stale age,
default thirty minutes. Beyond that the option is disabled rather than priced
from data nobody should trust. This is a deliberate trade of accuracy against
availability, and thirty minutes is the number that says how much.

**Failure degrades per currency, not per payment.** A missing BTC rate disables
BTC as a payment option and leaves LTC and native ETH offered. The payer loses a
choice; the shop does not lose the sale.

**Multi-provider fallback is out of scope.** A second rate source means a rule
for which one wins when they differ, and that rule is exactly the complexity
this product spends its budget avoiding — the same argument as
[ADR 0004](./0004-one-observation-provider-is-selected-and-never-mixed.md).

**The lock outlives the rate's accuracy on purpose.** A payment created at a
volatile moment and paid an hour later settles at the locked rate, and the
market movement is the operator's. Payment Expiration is the lever that bounds
that exposure, which is why it is configurable and defaults to one hour.
