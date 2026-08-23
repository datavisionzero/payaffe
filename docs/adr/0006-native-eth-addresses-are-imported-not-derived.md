# Native ETH Addresses Are Imported, Not Derived

The admin bulk-imports public Ethereum addresses into an Address Pool, and a
payment is assigned one when the payer selects native ETH. An assigned address
is never automatically handed out again, and when the pool runs dry native ETH
stops being offered as a payment option.

This exists because Ethereum does not have the mechanism BTC and LTC use.
[ADR 0005](./0005-payaffe-never-holds-a-key-that-can-spend.md) derives BTC and
LTC addresses from an extended public key, which yields fresh addresses without
any spending ability. Deriving ETH addresses inside the product would mean
holding a seed, and holding a seed is precisely the thing that ADR forbids. So
the derivation is moved out of the product entirely: whatever wallet the
operator already trusts generates the addresses, and `payaffe` receives only the
public half.

Reusing one ETH address was the obvious alternative and fails for the same
reason it fails everywhere else here — several open payments in the same amount
range make matching ambiguous. Reusing an assigned address after a delay was
considered more seriously and rejected because of late payments: a transaction
can legitimately arrive after expiration, and an address that has been recycled
by then would credit it to the wrong payment.

## Consequences

**Running out is a normal operating state, not a fault.** The pool has capacity,
the Admin UI shows it, a configurable threshold warns before it is empty, and an
empty pool degrades to "native ETH not offered" rather than to a failed payment.
`PAYAFFE_NATIVE_ETH_LOW_CAPACITY_THRESHOLD` is what the operator tunes.

**Refilling is a recurring operator chore**, and it is the honest cost of this
decision. It is documented in
[ethereum-address-pool.md](../operations/ethereum-address-pool.md) rather than
left to be discovered when the pool empties.

**Restore must not resurrect availability.** A database restored from backup has
to keep assigned addresses assigned; a restore that returns them to the pool
would hand a used address to a new payment. This is called out as a restore
requirement rather than assumed.

If ETH ever gains a watch-only derivation path the operator can supply, this is
the decision to reopen — the pool is a workaround for a missing mechanism, not a
preference.
