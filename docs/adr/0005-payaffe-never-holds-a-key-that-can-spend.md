# payaffe Never Holds a Key That Can Spend

There is no private key, no seed phrase, and nothing else in the database that
could move a coin. The product watches addresses and reports what arrived; it
cannot sweep, refund, or withdraw, and no admin action and no MCP tool can make
it.

This is the decision the rest of the product is shaped around rather than a
precaution added to it. Holding spending keys would make `payaffe` custodial,
and custody is not one feature — it is key generation, encrypted storage, backup
that is itself a theft target, restore that must not resurrect a spent key,
signing, fee estimation, and a liability posture that a self-hosted shop
operator has not signed up for. The product's whole case is that an operator can
run it next to their shop without any of that.

For BTC and LTC this costs almost nothing: a Watch-Only Wallet Source — an
extended public key the operator configures — derives receiving addresses
without any ability to spend from them. Native ETH has no equivalent, which is
[ADR 0006](./0006-native-eth-addresses-are-imported-not-derived.md).

## Consequences

**There are no refunds.** Not "not yet" in the sense of a backlog item: a refund
requires spending, and spending is the thing that is absent. An overpayment is
shown to the admin as an overpayment and resolved outside the product. An
integration that needs refunds needs a different product or an out-of-band
process.

**A static receiving address per currency was rejected**, even though it would
have been simpler than either address mechanism. With several payments open at
once and amounts in the same range, one address makes matching a guess — and the
matching is the product.

**The blast radius of a compromise is disclosure, not theft.** Someone who takes
the database gets payment history, addresses, hashed admin credentials, and
hashed integration tokens. They do not get funds, because the funds were never
reachable from here.

If custody is ever wanted, this is the first decision to reopen, and it should
be reopened as a question about custody — not arrived at by adding a key field
to a wallet source.
