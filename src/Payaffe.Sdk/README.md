# Payaffe.Sdk

Typed .NET client for the [payaffe](https://github.com/datavisionzero/payaffe)
Integration API. It lets a product accept cryptocurrency payments from its own
checkout, rendered by its own frontend, without sending the Payer to the hosted
Payer Page.

The package targets `net10.0`, contains no UI, and speaks `/api/v1` only.

## Trust boundary

The SDK belongs in your backend. The Integration API bearer token must not be
compiled into frontend code, returned to a browser, placed in a URL, or written
to a log. A browser-supplied Payment identifier or External Reference is not
proof that the caller owns an order: authorize your own Payer against your own
order before exposing Payment state, and use `externalReference` for
reconciliation rather than authorization.

## Creating a client

The client takes an externally managed `HttpClient`. It never creates a global
one, reads configuration implicitly, or rotates credentials for you.

```csharp
services.AddPayaffeClient(options =>
{
    options.BaseAddress = new Uri("https://payaffe.example.com");
    options.ApiToken = configuration["Payaffe:ApiToken"];
});
```

Registering several named clients keeps one Project's credential and Payment
addresses separate from another's:

```csharp
services.AddPayaffeClient("shop", options => { /* ... */ });
var client = serviceProvider.GetRequiredService<IPayaffeClientFactory>().CreateClient("shop");
```

## The payment flow

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

Render the QR code from `instruction.Uri` exactly as returned, and take the
amount and address from the returned fields. Do not rebuild either of them.

Retry rules the SDK does not hide:

- creation may be repeated only with the original body and original idempotency
  key, and the replay returns the same Payment;
- Currency Selection may be repeated with the same Payment and currency, and
  returns the same immutable instruction;
- a losing concurrent selection receives `payment.currency_already_selected`
  and must display the winning instruction rather than replace it.

## Showing the payment QR code

`PayaffePaymentQrCode` encodes the instruction in your process. Nothing is
fetched from Payaffe or from a QR service, so the code renders under your own
origin and appears in your own markup:

```csharp
var qr = PayaffePaymentQrCode.Create(payment.PaymentInstruction!, new PayaffeQrCodeOptions
{
    DarkColor = "currentColor",
    LightColor = null,
    AccessibleLabel = $"Scan to pay order {order.Number}",
});

return Results.Content(qr.ToSvg(), "image/svg+xml");
```

The payload is `instruction.Uri`, byte for byte. The helper never assembles a
URI from an address and an amount, because the amount belongs to a Rate Lock and
its precision is the API's to decide: eight decimal places for BTC and LTC, wei
for native ETH.

`ToSvg()` returns a standalone SVG sized in modules through its `viewBox`, so
CSS decides how large it is displayed. It references no font, image, script, or
remote origin, and carries no Payaffe branding. Colours may be hexadecimal
values or CSS colour keywords; anything else is refused rather than written into
your page. For a different output, `ToModuleMatrix()` hands you the symbol and
your own imaging stack renders it.

The defaults are error correction level M and a four-module quiet zone, which is
what wallets are tested against. Encoding uses
[Net.Codecrete.QrCodeGenerator](https://github.com/manuelbl/QrCodeGenerator)
(MIT), the SDK's only non-Microsoft dependency; it pulls in no imaging stack and
no native component.

## Reconciling

`PollPaymentAsync` reads Payment state with exponential backoff, bounded
jitter, and `Retry-After` as a lower bound. It stops at `completed`, `settled`,
or `expired`, and never busy-polls:

```csharp
await foreach (var state in payaffe.PollPaymentAsync(paymentId, cancellationToken: cancellationToken))
{
    order.Apply(state.Status);
}
```

Poll once per order, not once per open browser tab. `expired` is terminal but
is not a failure: a late transfer may still be completed or manually settled,
so treat only `completed` and `settled` as success.

## Verifying webhooks

Webhooks and polling are both needed, because either alone fails: a Delivery
can arrive before or after the state it describes, and Delivery is at-least-once.

Verify the raw request bytes before trusting anything in them, then deduplicate
by `EventId`:

```csharp
var body = await ReadRawBodyAsync(request);
var result = PayaffeWebhookVerifier.Verify(
    body,
    PayaffeWebhookHeaders.FromLookup(name => request.Headers[name]),
    endpointSecret,
    DateTimeOffset.UtcNow);

if (!result.IsValid)
{
    return Results.Unauthorized();
}

if (await _events.TryRecordAsync(result.Event!.EventId))
{
    await HandleAsync(result.Event);
}
```

Verifying a re-serialized body fails: the signature covers the bytes as they
arrived. It covers the timestamp and the body and nothing else, so branch on the
verified event rather than on the event-type header, which is routing
information only. The verifier does not host an endpoint, choose a web framework,
persist deduplication state, or dispatch handlers.

## Testing against a Test Mode installation

A payaffe installation started with `PAYAFFE_INSTALLATION_MODE=test` needs no
wallet, no blockchain provider and no exchange-rate key, and it cannot receive
real money: its Payment Addresses are testnet addresses, its native ETH
instructions name the Sepolia chain, and its rates are fixed. Point the client
at it like any other installation and drive the whole flow from your tests:

```csharp
var payment = await payaffe.CreatePaymentAsync(request, idempotencyKey, cancellationToken);
payment = await payaffe.SelectCurrencyAsync(payment.PaymentId, SupportedCurrency.Btc, cancellationToken);

// Exactly the expected amount; pass an amount to underpay or overpay.
await payaffe.SimulatePaymentAsync(payment.PaymentId, cancellationToken: cancellationToken);
```

`SimulatePaymentAsync` records a simulated transfer and returns at once. The
Payment then goes through `observed` and `completed` on the installation's own
schedule, with the same webhooks and the same polling results a real transfer
produces, so your handlers are the code under test rather than a special test
path. A second call tops up an underpayment; a call after `ExpiresAt` exercises
late acceptance. Pass an `idempotencyKey` if the call may be retried: without
one the client does not repeat a request whose outcome it cannot know, because a
second transfer would be an overpayment. Against a live installation the method
throws `PayaffeApiException` with `PayaffeErrorCode.TestModeUnavailable`.

Every Payment and every webhook event says where it came from: `Payment.TestMode`
and `PayaffeWebhookEvent.TestMode` are `true` only in a Test Mode installation.
A test installation can be configured with your production webhook URL and a
valid secret, so a correct signature does not prove the money was real. In
production, refuse to fulfil anything whose `TestMode` is `true`:

```csharp
if (result.Event!.TestMode && !_environment.IsDevelopment())
{
    return Results.BadRequest();
}
```

## Versions and upgrading

The package and the installation are versioned separately on purpose. SDK `1.x`
speaks Integration API `/api/v1`, and that is the only promise the two version
lines make each other: upgrading a payaffe installation never obliges you to take
a new package, and a fix in this client never claims a server release that did not
happen.

Within that, the package follows semantic versioning. A new Payment Status,
Supported Currency, option status or error code is a compatible change on both
sides, which is why the string-backed value types keep values they have never
heard of instead of throwing. Pin the major version and take minors freely:

```xml
<PackageReference Include="Payaffe.Sdk" Version="[0.3.0,1.0.0)" />
```

Coming from a hand-written HTTP client, the migration is mechanical and needs no
data change. Bearer tokens, Payment identifiers, External References, idempotency
records and Payer Page URLs all survive it, and payer links handed out before the
move keep working:

- replace your create, read and Currency Selection calls with the client's
  methods, keeping your existing idempotency keys — a replayed creation with the
  original key returns the same Payment, so the switch does not create a second
  one for an order in flight;
- replace a polling loop with `PollPaymentAsync`, which will not busy-poll and
  stops on a terminal status;
- replace hand-rolled signature checking with `PayaffeWebhookVerifier`, and keep
  your own deduplication store: the event identifiers you have already recorded
  stay valid;
- stop reading `payerPageUrl` if you were redirecting to it. It is still returned,
  and existing links still load, but an embedded checkout has no use for it.

A Payment that a payer already selected a currency for on the hosted page returns
that same instruction here. Neither surface can replace the other's selection.

## Errors

`PayaffeApiException` carries the HTTP status, the stable `Code`, the
`CorrelationId` to quote in a support request, field validation codes, and
`RetryAfter` when the response carried it. It never contains the bearer token
or the request body. Cancellation and caller-owned HTTP timeouts stay
distinguishable from API errors.

Unknown future values of Payment Status, Supported Currency, option status, and
error codes are preserved rather than rejected, because adding one is a
compatible change to `/api/v1`. Money is never deserialized through binary
floating point: minor units are integers and cryptocurrency amounts are strings.
