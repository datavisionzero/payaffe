# A Payment Is Created in Fiat, and payaffe Converts

An external system creates a payment by naming an amount in EUR or USD. It does
not name a coin, an amount of that coin, or an exchange rate. Which currencies
are offered, what the crypto amount is, and which address it goes to are all
decided inside `payaffe` afterwards.

The alternative — the integration submits an exact BTC amount — puts the rate
source, the rounding, the dust threshold, and the per-currency precision into
every shop that integrates. That is the same logic implemented once per
integrator, each version subtly different, and all of them needing a change when
the rate source does. Keeping it inside means the Integration API surface is a
fiat amount, an External Reference, and an Idempotency Key, and a shop can
integrate without learning anything about cryptocurrency.

## Consequences

**The product needs an exchange rate source, and therefore a rate failure
mode.** That is the cost this decision incurs directly, and what it implies is
worked out in
[ADR 0008](./0008-the-rate-locks-when-the-payer-picks-a-currency.md).

**Payment creation does not fix an amount.** It records the fiat amount and
which payment options are available; there is no coin and no address yet,
because the payer has not chosen. The status vocabulary reflects this rather
than hiding it — a freshly created payment is Pending Currency Selection.

**Crypto-denominated creation is not supported.** An integration that wants to
charge exactly 0.001 BTC cannot express that. This is a real limitation for
crypto-native callers and an acceptable one for the shops this product is for.

**Rounding and precision are per-currency and internal.** They are behaviour to
be tested, not contract to be published, and no integration should be able to
depend on the exact conversion arithmetic.
