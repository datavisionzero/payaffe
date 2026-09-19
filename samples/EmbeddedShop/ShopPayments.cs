using Microsoft.Extensions.Options;
using Payaffe.Sdk;

namespace EmbeddedShop;

/// <summary>
/// Everything this shop does with Payaffe. It lives entirely in the backend: the browser talks
/// to the endpoints in <c>Program.cs</c>, those endpoints talk to this, and this is the only
/// place the Integration API credential is used.
/// </summary>
internal sealed class ShopPayments(
    IPayaffeClientFactory clients,
    IOptions<ShopOptions> options,
    OrderStore orders,
    OrderReconciler reconciler,
    ILogger<ShopPayments> logger)
{
    public async Task<IResult> PlaceOrderAsync(
        string storefront,
        string customerId,
        ShopItem item,
        CancellationToken cancellationToken)
    {
        if (!TryGetStorefront(storefront, out StorefrontOptions? configuration))
        {
            return Results.NotFound();
        }

        ShopOrder order = orders.Create(storefront, customerId, item, configuration.FiatCurrency);

        // The order identifier is both the External Reference and the idempotency key. It is
        // stable, so a retried creation after a timeout returns the same Payment instead of
        // allocating a second one against the same order.
        string orderReference = order.OrderId.ToString("D");
        try
        {
            Payment payment = await clients.CreateClient(storefront).CreatePaymentAsync(
                new CreatePaymentRequest(configuration.FiatCurrency, item.PriceMinor, orderReference),
                orderReference,
                cancellationToken);
            order.Apply(payment, "created");
            return Results.Ok(OrderView.Of(order));
        }
        catch (PayaffeApiException exception)
        {
            return PaymentsUnavailable(exception, "creating a payment");
        }
    }

    public async Task<IResult> SelectCurrencyAsync(
        string storefront,
        Guid orderId,
        string customerId,
        string currency,
        CancellationToken cancellationToken)
    {
        // Authorization is the shop's, and it happens before anything is sent to Payaffe: the
        // browser supplied the order identifier, and that is not proof of anything.
        ShopOrder? order = orders.FindForCustomer(storefront, orderId, customerId);
        if (order?.PaymentId is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return Results.BadRequest(new { error = "currency_required" });
        }

        PayaffeClient client = clients.CreateClient(storefront);
        try
        {
            Payment payment = await client.SelectCurrencyAsync(
                order.PaymentId.Value,
                new SupportedCurrency(currency.ToUpperInvariant()),
                cancellationToken);
            order.Apply(payment, "currency selected");
            reconciler.Track(order);
            return Results.Ok(OrderView.Of(order));
        }
        catch (PayaffeApiException exception) when (
            exception.Code.Value is "payment.currency_already_selected")
        {
            // Two tabs of the same order raced, or a first attempt committed and its response
            // was lost. The selection that won is the one to display; replacing it is not an
            // option the API offers and not one the shop should want.
            Payment payment = await client.GetPaymentAsync(order.PaymentId.Value, cancellationToken);
            order.Apply(payment, "currency already selected");
            reconciler.Track(order);
            return Results.Ok(OrderView.Of(order));
        }
        catch (PayaffeApiException exception) when (
            exception.Code.Value is "exchange_rate.unavailable")
        {
            // No Rate Lock, so no instruction and no address. The Payment is untouched and the
            // customer can pick again, including the same currency.
            logger.LogWarning(
                "Rate unavailable for order {OrderId}, correlation {CorrelationId}.",
                order.OrderId,
                exception.CorrelationId);
            return Results.Json(
                new { error = "rate_unavailable", retryable = true },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (PayaffeApiException exception) when (exception.Code.Value is "payment.expired")
        {
            Payment payment = await client.GetPaymentAsync(order.PaymentId.Value, cancellationToken);
            order.Apply(payment, "expired before selection");
            return Results.Json(
                new { error = "payment_expired", retryable = false },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (PayaffeApiException exception)
        {
            return PaymentsUnavailable(exception, "selecting a currency");
        }
    }

    /// <summary>
    /// One Webhook Delivery. Nothing in the request is believed until the signature over the raw
    /// bytes verifies against this storefront's secret, and nothing is acted on twice.
    /// </summary>
    public async Task<IResult> HandleWebhookAsync(
        string storefront,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetStorefront(storefront, out StorefrontOptions? configuration))
        {
            return Results.NotFound();
        }

        // The bytes as they arrived. Binding the request to a model first and re-serializing it
        // would change the signature basis and reject every valid Delivery.
        using MemoryStream buffer = new();
        await context.Request.Body.CopyToAsync(buffer, cancellationToken);

        PayaffeWebhookVerificationResult verification = PayaffeWebhookVerifier.Verify(
            buffer.GetBuffer().AsSpan(0, (int)buffer.Length),
            PayaffeWebhookHeaders.FromLookup(name => context.Request.Headers[name]),
            configuration.WebhookSecret,
            DateTimeOffset.UtcNow);

        if (!verification.IsValid)
        {
            logger.LogWarning(
                "Rejected a webhook delivery {DeliveryId} for {Storefront}: {Reason}.",
                verification.DeliveryId,
                storefront,
                verification.RejectionReason);
            return Results.Unauthorized();
        }

        PayaffeWebhookEvent webhookEvent = verification.Event!;

        ShopOrder? order = orders.FindByExternalReference(
            storefront,
            webhookEvent.Payment.ExternalReference);
        if (order is null)
        {
            // Accepted, not rejected: a delivery this shop cannot place is not a delivery
            // Payaffe should keep retrying. The event is deliberately not recorded as handled,
            // so a redelivery still counts if the order turns up in between.
            logger.LogWarning(
                "No order for external reference {ExternalReference} in {Storefront}.",
                webhookEvent.Payment.ExternalReference,
                storefront);
            return Results.Ok(new { status = "ignored" });
        }

        // At-least-once means the same event will arrive again. Claiming it before acting is
        // what makes fulfilment happen once; in a product the claim and the fulfilment share one
        // database transaction.
        if (!orders.TryClaimEvent(storefront, webhookEvent.EventId))
        {
            return Results.Ok(new { status = "duplicate" });
        }

        order.Apply(webhookEvent.Payment, $"webhook {webhookEvent.EventType}");
        return Results.Ok(new { status = "accepted" });
    }

    private bool TryGetStorefront(string storefront, out StorefrontOptions configuration)
    {
        if (options.Value.Storefronts.TryGetValue(storefront, out StorefrontOptions? found))
        {
            configuration = found;
            return true;
        }

        configuration = null!;
        return false;
    }

    /// <summary>
    /// What the customer is told when Payaffe could not be reached or refused for a reason the
    /// shop has no answer for. The correlation identifier goes to the log, where support can
    /// quote it; the customer gets none of it.
    /// </summary>
    private IResult PaymentsUnavailable(PayaffeApiException exception, string activity)
    {
        logger.LogError(
            exception,
            "Payaffe refused while {Activity}: {Code}, correlation {CorrelationId}.",
            activity,
            exception.Code,
            exception.CorrelationId);
        return Results.Json(
            new { error = "payments_unavailable" },
            statusCode: StatusCodes.Status502BadGateway);
    }
}
