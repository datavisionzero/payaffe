# A Webhook Is Signed, Versioned, and at Least Once

An outgoing webhook crosses a trust boundary in the direction nobody watches:
the receiver is being told that money arrived, and acting on it. So every
delivery carries an HMAC-SHA256 signature over a canonical base — the timestamp,
a literal `.`, and the raw request body — in `Payaffe-Webhook-Signature`,
alongside `Payaffe-Webhook-Id`, `Payaffe-Webhook-Timestamp`,
`Payaffe-Webhook-Event-Type`, and `Payaffe-Webhook-Event-Version`. Receivers are
told to reject signatures outside the replay window.

Unsigned delivery was rejected on the obvious grounds. The less obvious decision
is treating the payload as a published contract rather than an internal DTO: the
MVP event types are `payment.created`, `payment.currency_selected`,
`payment.observed`, `payment.completed`, `payment.expired`, and
`payment.settled`, all at `event_version` 1, in a stable snake_case envelope
carrying `event_id`, `event_type`, `event_version`, `occurred_at`,
`correlation_id`, and a payment reference. External systems consume these
deliberately, so they are versioned deliberately, and the accepted shape of each
is snapshotted under `docs/contracts/webhooks/` and checked by a test.

Payloads are data-minimised: no bearer tokens, no webhook secrets, no provider
responses, no wallet material.

## Consequences

**Delivery is at-least-once, and `event_id` is how a receiver deduplicates.**
Exactly-once was never on the table — it would require a transaction the
receiver is not in. Out-of-order arrival is possible too, and the envelope
carries `occurred_at` so a receiver can tell.

**A payment state change never waits on a webhook.** The event is written to the
outbox in the same transaction as the state change and delivered afterwards by
the worker; see
[ADR 0013](./0013-a-payment-change-and-the-events-it-causes-share-one-transaction.md).
Synchronous delivery was rejected because it makes an unreachable receiver able
to decide whether a payment is durable.

**Manual resend uses the same signing path.** A resent delivery is not a special
case with its own code, because a special case is where a signing bug would
live.

**Adding a field is safe, changing one is not.** New optional fields go into
version 1; anything a receiver could be parsing changes the version, and the
contract snapshot is what makes that visible in review.
