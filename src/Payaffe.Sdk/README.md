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
arrived. The verifier does not host an endpoint, choose a web framework,
persist deduplication state, or dispatch handlers.

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
