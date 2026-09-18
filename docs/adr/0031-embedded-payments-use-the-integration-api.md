# Embedded Payments Use the Integration API

An embedding product uses the versioned Integration API for the whole Payment
flow: create and read a Payment, inspect its Payment Options, and select a
Supported Currency. Currency Selection is added to `/api/v1` as an
authenticated idempotent `PUT`; the unauthenticated Payer Page routes remain an
implementation surface for Payaffe's hosted page and are not an integration
contract.

The existing v1 Payment representation already exposes Payment Options and the
selected currency, amount, and address. Adding one endpoint and additive
Rate-Lock and Payment-Instruction objects preserves existing request semantics,
identifiers, bearer tokens, Payer Page URLs, and response fields. A new major
version would create a needless parallel lifecycle and SDK for a change that
does not reinterpret any existing field.

## Consequences

**Hosted and embedded are presentation choices.** Payaffe's Payer Page and a
product-owned payment screen invoke the same application operation and produce
the same durable Rate Lock, address assignment, Payment Event History, Webhook
Events, and Payment Status. An embedding product can ignore `payerPageUrl`; it
does not need to scrape, frame, redirect through, or otherwise depend on the
hosted page.

**The target product's backend is the trust boundary.** An Integration API
Credential never enters browser, mobile, desktop, or other untrusted client
code. Payaffe authenticates the credential and isolates its Project; the target
product authenticates its own Payer and proves that the Payer may view or act
on the external order mapped to the Payment. Payaffe does not infer that
customer authorization from `paymentId` or `externalReference`.

**Selection is a compare-and-set operation.**
`PUT /api/v1/payments/{paymentId}/currency-selection` atomically selects the
first Supported Currency. Repeating the same selection returns the original
immutable result. A competing different selection loses with a conflict and
cannot replace its Rate Lock or Payment Address. This makes a retry after an
ambiguous timeout safe without another idempotency-key store.

**Payment instructions are server-authored.** The response carries an exact
decimal amount, its atomic-unit integer, network, address, and wallet URI. The
SDK and embedding UI display or encode that URI; they do not reproduce
currency-specific URI rules. BTC follows BIP 321, native ETH follows ERC-681,
and LTC uses the documented BIP-21-shaped Litecoin convention.

**The first SDK is deliberately small.** A `net10.0` `Payaffe.Sdk` package
wraps the v1 HTTP contract, typed errors, cancellation, and bounded polling. It
contains no UI, payment lifecycle rules, credential persistence, customer
authorization, ASP.NET middleware, webhook receiver, or generated Payaffe
server internals. Webhook signature verification may be a small independent
helper in the same package because it operates only on headers and raw bytes.

The HTTP details are in
[integration-api-contract.md](../architecture/integration-api-contract.md), and
the consumer and SDK rules are in
[embedded-payment-sdk-baseline.md](../architecture/embedded-payment-sdk-baseline.md).
