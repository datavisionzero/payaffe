# Integration API Contract

This document records the target contract baseline for the public Integration
API. It complements the product requirements and ADRs; generated OpenAPI and
tests become the technical source of truth once implementation exists.

## Scope

The Integration API is the stable HTTP API used by external systems to create,
inspect, and complete the setup of Payments. It supports both redirecting a
Payer to Payaffe's hosted Payer Page and rendering an embedded payment flow in
the external product.

It is separate from:

- Admin UI backend routes,
- Payer Page backend routes,
- admin MCP tools,
- outgoing Webhook Delivery payloads.

## Versioning And Resources

The MVP Integration API uses path-based major versioning:

- `/api/v1/...`

Breaking changes require a new major path such as `/api/v2/...`. Minor and
patch changes are represented through backward-compatible schema and behavior
changes, not through path changes.

The public Payment resource paths are:

- `POST /api/v1/payments`;
- `GET /api/v1/payments/{paymentId}`;
- `PUT /api/v1/payments/{paymentId}/currency-selection`.

The Currency Selection endpoint and additive Payment response fields extend
v1. They do not change the meaning or type of an existing request or response
field. Removing `payerPageUrl`, changing amount units, changing a nullable
field's lifecycle guarantee, moving Project selection into the request, or
reinterpreting an existing status would require `/api/v2`.

Additional Integration API endpoints must be designed as part of the public
contract before implementation.

## Authentication

Integration API requests use static bearer tokens issued through Integration
API Credentials.

Each Integration API Credential belongs to exactly one Project. Successful
authentication resolves that Project as server-side request context; callers do
not choose it with a header, query parameter, request body, or path segment. A
credential may create, inspect, and select the currency of Payments in its
Project and cannot access resources in another Project. Within one Project,
credentials do not partition Payment visibility.

The Integration API is not cookie-authenticated and does not use CSRF tokens.
Browser-facing Admin routes have their own CSRF requirements.

Integration API Credentials are backend credentials. They must not be sent to
the Payer's browser or embedded in a mobile or desktop application. An
embedding product authenticates and authorizes its own customer before its
backend maps that customer's order to a Payaffe Payment. A Payment identifier
or External Reference is not customer authorization evidence.

Authentication failures use generic responses that do not disclose whether a
token exists, is disabled, or has the wrong shape unless the detail is safe for
the caller.

Integration API requests use a fixed-window default rate limit of 120 requests
per minute for each route, source IP, and bearer-token fingerprint partition.
Requests without bearer-token evidence are partitioned by route and source IP.
The bearer token itself must not be stored in the rate-limit key, logs, Audit
Log entries, traces, metrics, or error responses. The source IP is the one the
host believes in, which behind a reverse proxy means the proxy's until the
proxy is named
([ADR 0032](../adr/0032-the-client-address-is-the-connection-until-a-proxy-is-named.md));
the token fingerprint is what keeps an authenticated caller's partition its own
either way.

## Payment Creation

`POST /api/v1/payments` creates a Payment.

The request must include:

- Fiat Amount;
- External Reference;
- `Idempotency-Key` header.

The request may include:

- Payment Context Fields;
- Return URL.

The response includes at least:

- Payment identifier;
- current Payment Status;
- Payer Page URL;
- Payment Expiration;
- Payment Options.

`payerPageUrl` remains present for compatibility even when the creating system
intends to render the flow itself.

Payment Creation idempotency is scoped to the Integration API Credential within
its Project:

- the same `Idempotency-Key` with the same Payment Creation data returns the
  same Payment;
- the same `Idempotency-Key` with different Payment Creation data returns an
  idempotency conflict error.

The caller chooses one stable key per intended Payment before its first
attempt. A timeout is retried with the same body and key. The server returns
`201 Created` when it created the Payment and `200 OK` when it replayed the
stored result.

## Payment Representation

The create, read, and Currency Selection operations return one canonical
Payment representation. Existing fields retain their v1 meaning. The target
representation includes:

- `paymentId`, `externalReference`, `status`, `createdAt`, and `updatedAt`;
- `fiatCurrency` and `fiatAmountMinor`;
- `expiresAt` and `lateAcceptanceEndsAt`;
- `payerPageUrl` and nullable `returnUrl`;
- `paymentOptions`;
- nullable `selectedCurrency`, `expectedCryptoAmount`, `paymentAddress`,
  `rateLock`, and `paymentInstruction` before Currency Selection;
- nullable `observedTotal`, `confirmedEligibleTotal`, `observedAmountState`,
  `completedAt`, and `settledAt` as the Payment progresses.
- `testMode`, `true` when the installation is in Test Mode and the Payment is
  simulated
  ([ADR 0033](../adr/0033-a-test-installation-simulates-its-external-truth.md)).
  A production integration must not fulfil a Payment whose `testMode` is
  `true`.

Payment Status values are:

- `pending_currency_selection`;
- `waiting_for_payment`;
- `observed`;
- `completed`;
- `expired`;
- `settled`.

`observedAmountState` is separate from Payment Status and is one of `none`,
`underpaid`, `exact`, or `overpaid`. It describes totals only; a client must not
infer completion from it. An underpayment inside tolerance can be completed,
and an overpayment does not create a refund obligation.

Each Payment Option contains `supportedCurrency`, `status`, nullable
`unavailableReasonCode`, and `checkedAt`. Status is `available` or
`unavailable`. Availability is advisory until selection: the server rechecks
the exchange rate, Payment Address supply, Blockchain Observation, Project
status, and expiration before issuing an instruction. A transiently
unavailable option may become available on a later read; a client must not cache
option state past the Payment's expiration.

## Amount And Unit Representation

Fiat Amounts remain integer minor units. EUR and USD have two decimal places,
so `1999` means EUR 19.99 or USD 19.99. The JSON value is an integer; generated
clients must preserve signed 64-bit precision.

Cryptocurrency amounts and exchange rates are JSON strings, never JSON
floating-point numbers. A decimal value uses ASCII digits, an optional decimal
point, no sign, grouping separator, or exponent, no leading zero except before
the decimal point, and at most:

- 8 fractional digits for BTC;
- 8 fractional digits for LTC;
- 18 fractional digits for native ETH.

Trailing fractional zeros are allowed and do not change the value. Every
Payment Instruction also carries `amountAtomic`, a canonical unsigned base-10
integer string with no leading zeros except the value `0`: satoshis for BTC,
litoshis for LTC, and wei for native ETH. `amount` and `amountAtomic` must
represent exactly the same value. Consumers compare or calculate with decimal
or integer types, never binary floating point.

The immutable Rate Lock contains:

- `fiatCurrency` and `fiatAmountMinor`;
- `supportedCurrency`;
- `expectedCryptoAmount` and `expectedCryptoAmountAtomic`;
- `fiatPerCryptoUnit` as a decimal string;
- `source`, `rateObservedAt`, `lockedAt`, and `validUntil`.

`validUntil` equals the Payment Expiration. A Stale Rate remains explicit in
the snapshotted source or equivalent stable metadata. Once selected, no retry,
poll, Project configuration change, or later market rate may alter this object.

## Payment Instruction And QR Payload

After Currency Selection, `paymentInstruction` contains:

- `supportedCurrency`;
- `network`;
- nullable `chainId`, present for native ETH;
- `amount` and `amountAtomic`;
- `paymentAddress`;
- `uri`;
- `expiresAt`.

The server constructs `uri`; hosted and embedded clients use it byte-for-byte
as the link target and QR payload. They must not reconstruct it from display
text. The formats are:

- BTC follows
  [BIP 321](https://github.com/bitcoin/bips/blob/master/bip-0321.mediawiki):
  mainnet and legacy-address instructions use
  `bitcoin:<address>?amount=<amount>`, while a testnet SegWit instruction uses
  `bitcoin:?tb=<address>&amount=<amount>`;
- LTC: `litecoin:<address>?amount=<amount>`, the documented Payaffe
  compatibility convention shaped like BIP 21 and using decimal LTC;
- native ETH:
  `ethereum:<hex-address>@<decimal-chain-id>?value=<amountAtomic>`, following
  [ERC-681](https://eips.ethereum.org/EIPS/eip-681) with an integer wei value.

Payaffe does not add customer names, free text, External References, Return
URLs, or callback parameters to wallet URIs. The network and native ETH chain
ID are snapshotted with the address assignment. URI generation rejects an
address/network mismatch rather than emitting an ambiguous instruction.

## Authenticated Currency Selection

`PUT /api/v1/payments/{paymentId}/currency-selection` requires the same bearer
authentication as create and read. Its body is:

```json
{
  "supportedCurrency": "BTC"
}
```

Supported Currency input is case-insensitive and the response uses canonical
`BTC`, `LTC`, or `ETH`. The operation is an atomic first-write-wins selection:

- the first valid request assigns one Payment Address, captures one Rate Lock,
  stores one Payment Instruction, and returns `200 OK`;
- a repeat for the selected currency returns `200 OK` with the original
  immutable Payment representation;
- a concurrent or later request for a different currency returns `409 Conflict`
  with `payment.currency_already_selected` and does not allocate another
  address or Rate Lock;
- an unavailable rate, address, or observation path returns `409 Conflict`
  without changing the Payment, so the same selection may be tried later;
- an expired Payment returns `409 Conflict` and is not selected.

`Idempotency-Key`, `ETag`, and `If-Match` are not used for Currency Selection.
The singleton `PUT` plus the stored selected currency form its idempotency and
concurrency contract. A client that receives an ambiguous transport timeout
repeats the same request; it never changes currency as part of a retry.

## Payment Polling

`GET /api/v1/payments/{paymentId}` returns the current Payment state for
external-system polling and reconciliation.

The response includes the data needed by an external system to decide whether
the Payment is still waiting, observed, completed, expired, settled, or
otherwise requires manual follow-up according to the product requirements.

Polling is the recovery path when Webhook Deliveries are missed or delayed.

`paymentId` is resolved together with the authenticated Project. A valid
identifier from another Project receives the same safe not-found response as an
unknown identifier.

Clients start at a two-second interval, apply bounded exponential backoff and
jitter up to fifteen seconds, and treat `Retry-After` as a lower bound after
`429` or `503`. Authentication, authorization, validation, not-found, and
conflict responses are not polling retries. Polling stops on `completed`,
`settled`, or `expired`, but the integration continues to accept Webhook Events
and deliberate reconciliation reads for late-payment and manual-resolution
workflows.

## Test Mode Simulation

A Test Mode installation
([ADR 0033](../adr/0033-a-test-installation-simulates-its-external-truth.md))
adds one route, and only there:
`POST /api/v1/payments/{paymentId}/simulated-transactions`. In a live
installation the route is not mapped and answers like any unknown path.

It records a Simulated Transaction for a Payment of the authenticated Project
that is `waiting_for_payment` or `observed`. The optional JSON body carries
`amount`, a decimal string in the selected currency; without it the simulated
payer sends exactly the expected amount. A smaller or larger amount exercises
Underpayment and Overpayment, a second call tops an underpayment up, and a call
after `expiresAt` exercises the Late Acceptance Window. The response is `201`
with the recorded transaction hash and amount. Nothing about the Payment changes
in that response: the simulated Blockchain Observation reports the transaction
on its next poll with no confirmations and on the poll after that fully
confirmed, and the Payment, its webhooks and its polling then behave exactly as
for a real transaction.

An optional `Idempotency-Key` header makes a retry safe: the same key for the
same Payment returns the first transaction with `200`, and the same key with a
different amount is `409 idempotency.conflict`. Without a key every call records
another transaction. An unknown Payment or one from another Project is
`404 payment.not_found`; a Payment without a Payment Instruction or already
finished is `409 payment.not_waiting_for_payment`; an amount that is not a
positive decimal within the currency's precision is `400` with `amount.invalid`
or `amount.not_positive` under `errors.amount`.

## Project And Upgrade Compatibility

The introduction of Projects does not remove or reinterpret an existing
`/api/v1` request or response field. Existing credentials and Payments are
assigned to one default Project, and credentials may still inspect all
Payments in that Project. Existing bearer tokens, identifiers, Payer Page URLs,
idempotency keys, and Webhook contracts remain valid.

Existing unexpired Payments in `pending_currency_selection` may be selected
through the authenticated endpoint. Existing Payer Page links continue to use
the hosted flow. A selection made through either surface is the one immutable
selection seen by both surfaces. Integration consumers are never moved onto or
asked to call `/api/payer/**`.

A disabled Project rejects Payment Creation with `project.disabled` while
allowing authenticated polling and Currency Selection for existing Payments.
An archived Project is read-only and rejects Currency Selection. A future
design that lets a credential span Projects or lets a caller select Project
context requires a new public-contract decision and may require `/api/v2`.

## Errors

Errors use `ProblemDetails`.

Error responses include safe standard fields and should include:

- `correlationId`;
- stable machine-readable `code` when the client can act on the error.

The public error-code set includes at least:

- `authentication.required`;
- `authentication.invalid`;
- `authorization.denied`;
- `validation.failed`;
- `payment.not_found`;
- `project.disabled`;
- `project.archived`;
- `idempotency.conflict`;
- `payment_options.unavailable`;
- `payment.currency_already_selected`;
- `payment.expired`;
- `supported_currency.unsupported`;
- `exchange_rate.unavailable`;
- `payment_address.unavailable`;
- `blockchain_observation.unavailable`;
- `rate_limited`;
- `unexpected_error`.

`payment_options.unavailable` applies when creation cannot offer any Supported
Currency. The three specific unavailable codes identify a failed selection and
also appear as Payment Option reason codes. Problem details do not expose a
credential, secret, provider payload, or another Project's resource state.

Validation errors use `validation.failed` with an `errors` extension. The
extension maps field paths to lists of machine-readable validation codes.
Validation codes are API contract values, not localized UI strings.

## Pagination, Sorting, And Filtering

The MVP does not require a list endpoint for Integration API polling.

If a future Integration API list endpoint is added, it should use cursor-based
pagination by default with:

- `limit`;
- `cursor`;
- `sort`;
- explicitly documented filter parameters.

Free sorting or filtering over arbitrary database, entity, or DTO fields is not
allowed.

## Optimistic Concurrency

Currency Selection does not use general resource-version concurrency. Its
domain rule is narrower: a Payment can transition from no selected currency to
one selected currency exactly once, and the database operation atomically
enforces that compare-and-set. Same-value replay succeeds and different-value
replay conflicts.

Future Integration API mutation endpoints must still decide whether lost
updates or stale client state can cause incorrect behavior. If they can and no
equally explicit domain idempotency rule applies, the endpoint must use opaque
`ETag` values and require `If-Match` on writes.

## OpenAPI And Contract Diff

The ASP.NET Core implementation must generate OpenAPI from the implemented API.
OpenAPI is not maintained manually as a second technical truth.

The generated Integration API description must expose:

- versioned paths;
- request and response schemas;
- `ProblemDetails` responses;
- validation error structure;
- bearer-token authentication requirements;
- required Payment Creation idempotency headers;
- the Currency Selection body, idempotent replay, and conflict responses;
- exact amount strings, atomic-unit strings, Rate Lock, Payment Instruction,
  network, chain ID, and URI fields;
- pagination, sorting, and filtering parameters when list endpoints exist;
- `ETag` and `If-Match` headers when concurrency-controlled mutations exist.

CI must:

- verify that OpenAPI generation succeeds;
- compare the generated OpenAPI document against the last accepted contract
  snapshot;
- require a visible major-version decision for breaking changes.

The document a Test Mode installation serves is the live document plus the
Test Mode route. It is kept as its own accepted snapshot,
`openapi.v1.test-mode.json`, beside `openapi.v1.json`, so the live snapshot
stays exactly what a production integration can call.

The accepted snapshot under `docs/contracts/integration-api/` changes only when
the implementation and its contract tests change. Architecture work does not
edit a generated snapshot by hand.
