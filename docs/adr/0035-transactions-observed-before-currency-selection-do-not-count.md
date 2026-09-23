# Transactions Observed Before Currency Selection Do Not Count

A Payment Address is assigned when the Payer selects a Supported Currency, and
until then nobody has been told to pay it. An address can still carry history:
the derivation cursor restarts at the configured starting index after a
database reset or restore, an extended public key may be used by a wallet
elsewhere, and an imported native ETH address may have received transfers
before it was imported. Eligibility had an upper bound, the Late Acceptance
Window, and no lower bound, so an old incoming transaction on a newly assigned
address completed the Payment without the Payer paying. So a Blockchain
Transaction whose Observed Payment Time lies more than two hours before
currency selection is not a Matching Blockchain Transaction of that Payment.
It is recorded once as an Address History Alert, shown to Admins and written
to the log, and it never counts toward completion.

The tolerance exists because Observed Payment Time is usually the block
timestamp, and Bitcoin block timestamps may trail real time by more than an
hour. A lower bound at the exact moment of selection would reject a payment
made immediately after selection in an early-stamped block.

Checking the address with the provider at assignment time was rejected. It
adds a provider call to currency selection, where the Payer is waiting and a
provider outage would block paying altogether, and the same history is found
by the first poll anyway.

## Consequences

**An Address History Alert is a signal to the operator, not a payment state.**
The Payment stays as it was, typically waiting for payment. The alert says the
address has been used before, which usually means a wallet source or address
pool needs attention; the Admin may settle the Payment manually if the funds
were in fact the Payer's.

**Selection time is recorded on the Payment.** Payments selected before the
change take it from their Rate Lock.
