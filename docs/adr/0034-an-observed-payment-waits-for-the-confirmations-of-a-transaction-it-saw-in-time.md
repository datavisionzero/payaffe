# An Observed Payment Waits For The Confirmations Of A Transaction It Saw In Time

A Blockchain Transaction observed inside Payment Expiration or the Late
Acceptance Window counts toward the Payment whenever it confirms. Expiry did
not follow that rule: an Observed Payment was expired at the end of the Late
Acceptance Window like an unpaid one, polling stopped, and `payment.expired`
went out although the money was on its way. A payer who broadcasts shortly
before the deadline, a low-fee transaction that sits in the mempool, or a Late
Acceptance Window of zero all end that way. So an Observed Payment with a
Matching Blockchain Transaction observed in time is not expired when the window
ends. It keeps being polled until it completes or until a Confirmation Wait
after the window ends, 72 hours by default and configurable per installation;
then it expires as before.

Waiting without a limit was rejected. A transaction that is double-spent or
dropped from the mempool never confirms, and the Payment would stay observed
for ever with nobody told. A transaction that disappears from the provider is
therefore not a separate case: it never confirms, and its Payment expires when
the Confirmation Wait ends. Expiring on the first poll that misses it was also
rejected, because a provider that lags or truncates an address history would
turn a paid Payment into an expired one.

## Consequences

**A Payment without a selected currency expires at Payment Expiration.** It has
no Payment Address, so nobody can pay it, and currency selection is already
refused from that moment. The Late Acceptance Window does not apply to it, and
`payment.expired` no longer arrives a day after the Payer was told the Payment
had expired.

**The payment status set does not change.** The waiting Payment stays
`observed`; the operator sees it in the Admin UI as before and may settle it
manually. Only the moment it becomes `expired` moves.

**The wait is an installation setting, not a Project policy.** It bounds how
long a provider is polled for an address, which is an installation-wide cost,
and it is validated at startup between zero and 30 days. Zero restores expiry
at the end of the Late Acceptance Window.
