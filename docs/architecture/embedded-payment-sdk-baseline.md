# Embedded Payment And .NET SDK Baseline

This document defines how another product embeds Payaffe's payment flow without
depending on the hosted Payer Page. It follows
[ADR 0031](../adr/0031-embedded-payments-use-the-integration-api.md) and the
[Integration API contract](integration-api-contract.md).

## Trust Boundary

An embedding product calls Payaffe only from its trusted backend. The target
product must:

- keep the Integration API bearer token in backend secret storage;
- authenticate its own Payer before exposing order or Payment state;
- authorize every create, read, selection, and polling action against its own
  order-to-Payer relationship;
- persist the Payaffe `paymentId` with its own order and use
  `externalReference` for reconciliation, not authorization;
- return only the Payment fields its frontend needs;
- treat Payaffe Webhooks as untrusted until their signature and replay window
  have been verified, then deduplicate them by `event_id`.

A browser-supplied `paymentId`, `externalReference`, Supported Currency, or
Return URL is not proof that the caller owns an order. Payaffe authorizes the
credential to its Project, but cannot authorize the target product's customer.

The bearer token must not be compiled into frontend code, returned to a
browser, placed in a URL, or written to logs. A direct browser-to-Payaffe SDK is
therefore out of scope.

## Headless Flow

The complete embedded flow uses only `/api/v1`:

1. The target backend creates a Payment with a stable `Idempotency-Key` and
   stores the returned `paymentId` against its order.
2. It returns the Fiat Amount, expiration, status, and current Payment Options
   needed by its own UI. `payerPageUrl` may be ignored.
3. After authorizing its Payer, it submits the chosen Supported Currency to the
   authenticated Currency Selection endpoint.
4. It displays the returned Payment Instruction. The QR payload is exactly the
   returned `uri`; amount and address copy actions use the returned fields.
5. It polls Payment state with bounded backoff and consumes signed Webhook
   Events. Either channel may arrive first, and polling is the reconciliation
   path.
6. It treats `completed` and `settled` as successful terminal outcomes and
   `expired` as a terminal outcome that may still require later reconciliation
   during the Late Acceptance Window or manual handling.

The target product must not call `/api/payer/**`. Those routes use the Payer
Page identifier as a capability and exist only for Payaffe's hosted page.

## SDK Package And Surface

The supported SDK is an independently versioned NuGet package named
`Payaffe.Sdk`, targeting `net10.0`. Its first public surface is equivalent to:

```csharp
public sealed class PayaffeClient
{
    Task<Payment> CreatePaymentAsync(
        CreatePaymentRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Payment> GetPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default);

    Task<Payment> SelectCurrencyAsync(
        Guid paymentId,
        SupportedCurrency currency,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<Payment> PollPaymentAsync(
        Guid paymentId,
        PaymentPollingOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

The concrete implementation accepts an externally managed `HttpClient`, API
base address, and bearer token. It does not create a global `HttpClient`, read
application configuration implicitly, or own credential rotation. All methods
accept cancellation. Public transport models are immutable and nullable where
the HTTP lifecycle makes a field unavailable.

String-backed extensible value types represent Payment Status, Supported
Currency, option status, and machine-readable error codes. Deserialization must
retain unknown future values instead of throwing, because adding a status or
code can be a compatible v1 change. JSON numbers used for money are never
deserialized through binary floating point.

`PayaffeApiException` exposes HTTP status, stable `code`, `correlationId`, field
validation codes, and `Retry-After` when present. It must not include the
bearer token or full request body in its message. Cancellation and caller-owned
HTTP timeouts remain distinguishable from API errors.

The SDK may include a UI-independent Webhook signature verifier that accepts
the raw request bytes, Payaffe headers, shared secret, current time, and allowed
clock skew. It does not host an HTTP endpoint, choose a web framework, persist
deduplication state, or dispatch business handlers.

The SDK may include a payment QR helper. It encodes the Payment Instruction URI
in the consuming process, so no request leaves the target product to produce a
code and nothing is fetched from a Payaffe origin or a QR service. The payload
is the returned `uri` unchanged: the helper must not assemble a URI from an
address and an amount, recompute a Rate Lock, or round a cryptocurrency amount.
Its default output is a standalone SVG with no font, image, script, remote
reference, or Payaffe branding, and it also exposes the module matrix so a
product can render the symbol with its own imaging stack. Presentation values a
product supplies are validated before they reach markup it serves.

SDK `1.x` targets Integration API `/api/v1`. SDK semantic-version changes do
not create an API major version, and an API v2 does not silently change the
base path used by an installed SDK major.

The package version lives in the SDK project file, not in the product version
every host reports, and the package is released from its own `sdk-v<version>`
tag. An installation release must not oblige an integrator to take a new
package, and a client-only fix must not claim a server release that never
happened.

The package is validated as a package, not only as a project: continuous
integration and the release both pack it, assert that it carries exactly one
target framework and no planning or credential content, and build and run a
consumer that has none of this repository's build files. Package documentation
covers installation, the flow, errors, the QR helper, webhook verification, and
the upgrade path for an integration that predates the package.

A representative product-backend flow remains UI-independent:

```csharp
var payment = await payaffe.CreatePaymentAsync(
    new CreatePaymentRequest("EUR", 1999, order.Id),
    idempotencyKey: order.PaymentAttemptId,
    cancellationToken);

AuthorizePayerForOrder(currentPayer, order, payment.PaymentId);

payment = await payaffe.SelectCurrencyAsync(
    payment.PaymentId,
    SupportedCurrency.Btc,
    cancellationToken);

var instruction = payment.PaymentInstruction
    ?? throw new InvalidOperationException("Selection returned no instruction.");

return new PaymentView(
    instruction.Amount,
    instruction.PaymentAddress,
    instruction.Uri,
    instruction.ExpiresAt);
```

The product's frontend renders `PaymentView`; it never receives the
`PayaffeClient` or its bearer token.

## Polling And Retry

The SDK does not hide an infinite retry loop. `PollPaymentAsync` defaults to:

- an initial two-second interval;
- exponential growth capped at fifteen seconds;
- bounded jitter so many clients do not synchronize;
- `Retry-After` as a lower bound after `429` or `503`;
- no retry for authentication, authorization, validation, not-found, or
  conflict responses;
- completion when `completed`, `settled`, or `expired` is observed;
- immediate cancellation when the caller's token is cancelled.

Callers may configure the interval bounds but cannot configure zero-delay busy
polling. A target backend should normally poll once for all viewers of an order,
not once per open browser tab.

Creation may be retried only with the original body and original
`Idempotency-Key`. Currency Selection may be retried with the same Payment and
Supported Currency because its `PUT` semantics are idempotent. The SDK never
automatically retries with a new key or a different Supported Currency.

## Consumer Scenarios

The examples below are acceptance scenarios for the HTTP contract and SDK.

| Scenario | Expected consumer behavior |
| --- | --- |
| Create through completion | Create once, present available options, select once, display the returned instruction, then reconcile `pending_currency_selection` → `waiting_for_payment` → `observed` → `completed` by webhook and polling. |
| Currency unavailable | Keep other `available` options selectable. Render the stable reason code for an unavailable option; do not manufacture an amount or address. |
| Rate unavailable during selection | Receive `409 exchange_rate.unavailable`; retain `pending_currency_selection`, refresh the Payment, and allow a later retry. |
| HTTP timeout during creation | Repeat the same request and `Idempotency-Key`; accept either the original `201` result or the replayed `200` result as the one Payment. |
| HTTP timeout during selection | Repeat the same `PUT`; a committed first call returns the same immutable instruction, while an uncommitted call may succeed on retry. |
| Concurrent different selections | The first committed currency wins. The losing request receives `409 payment.currency_already_selected`, reads the Payment, and must display the winning instruction rather than attempt replacement. |
| Expired before selection | Receive `409 payment.expired`, stop offering selection, and reconcile status. No Rate Lock or address is created. |
| Late payment | Continue accepting Webhooks and reconciliation reads after `expired`; do not infer success until Payaffe reports `completed` or `settled`. |
| Underpayment | Display `observedAmountState=underpaid` and totals. Await another Matching Blockchain Transaction or manual Settlement unless Payaffe completes it inside tolerance. |
| Overpayment | Treat a `completed` Payment as successful while retaining `observedAmountState=overpaid` for support and reconciliation; do not promise an automatic refund. |

Contract tests must cover these scenarios using the generated OpenAPI models,
the packaged SDK, and a server test host. A no-hosted-page end-to-end test must
prove that create, selection, instruction display data, observation,
completion, Webhook verification, and polling work without visiting a
`payerPageUrl`.

## Existing Payments And Payer Links

Existing bearer tokens, Payment identifiers, External References, idempotency
records, and Payer Page URLs survive the upgrade. Existing Payer links continue
to load and may finish active Payments through the hosted flow.

An existing unexpired `pending_currency_selection` Payment may instead be
selected through the new authenticated endpoint by a credential in its owning
Project. A Payment already selected through either surface returns the same
instruction when the selected currency is repeated. Neither surface may
replace the first selection.

The internal Payer routes remain available for retained links even after an
embedding product adopts the SDK. Their eventual removal is a separate hosted
page compatibility decision; consumers are never directed to use them as an
SDK substitute.
