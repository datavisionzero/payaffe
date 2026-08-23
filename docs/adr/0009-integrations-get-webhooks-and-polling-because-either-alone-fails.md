# Integrations Get Webhooks and Polling, Because Either Alone Fails

An external system learns that a payment changed either by receiving a webhook
or by asking. Both are supported, and the second is not a legacy path kept
around — it is the documented recovery route for when the first does not arrive.

Webhooks alone assume the receiver is reachable, publicly addressable, and up.
The shops this product integrates with are small, sometimes behind a
residential connection, and occasionally down for an afternoon; a missed
delivery that exhausts its retries would otherwise leave the shop permanently
wrong about a payment it was paid for. Polling alone is worse in the other
direction: every integration becomes a timer, latency becomes an interval, and
an idle installation still gets traffic.

Together the failure modes cancel. The webhook carries the news promptly, and
`GET /api/v1/payments/{paymentId}` is always the truth if the news was lost.

## Consequences

**Payment state has to be queryable, not just emittable.** Every state an event
announces must be readable afterwards from the payment resource, or the recovery
path does not actually recover anything.

**Delivery is at-least-once and receivers must be idempotent**, which is a
requirement placed on the integrator and therefore has to be documented as one.
The event envelope carries `event_id` for exactly this, and
[ADR 0010](./0010-a-webhook-is-signed-versioned-and-at-least-once.md) is where
that contract is settled.

**Rate limits have to tolerate polling.** An integration that lost a webhook is
supposed to poll, so the Integration API's limits are sized for it rather than
tuned as if polling were abuse.

**A terminally failed delivery is an admin-visible state.** Retries end, and
when they do the delivery is shown as failed with a manual resend available —
the operator gets to see that an integration is out of sync rather than having
it silently absorbed.
