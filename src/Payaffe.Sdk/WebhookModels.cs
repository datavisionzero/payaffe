namespace Payaffe.Sdk;

public static class PayaffeWebhookHeaderNames
{
    public const string DeliveryId = "Payaffe-Webhook-Id";

    public const string Timestamp = "Payaffe-Webhook-Timestamp";

    public const string Signature = "Payaffe-Webhook-Signature";

    public const string EventType = "Payaffe-Webhook-Event-Type";

    public const string EventVersion = "Payaffe-Webhook-Event-Version";
}

public static class PayaffeWebhookEventTypes
{
    public const string PaymentCreated = "payment.created";

    public const string PaymentCurrencySelected = "payment.currency_selected";

    public const string PaymentObserved = "payment.observed";

    public const string PaymentCompleted = "payment.completed";

    public const string PaymentExpired = "payment.expired";

    public const string PaymentSettled = "payment.settled";
}

public sealed record PayaffeWebhookHeaders(
    string? DeliveryId,
    string? Timestamp,
    string? Signature,
    string? EventType = null,
    string? EventVersion = null)
{
    /// <summary>
    /// Reads the Payaffe headers through a caller-supplied lookup so the verifier stays
    /// independent of any web framework's header collection.
    /// </summary>
    public static PayaffeWebhookHeaders FromLookup(Func<string, string?> getHeaderValue)
    {
        ArgumentNullException.ThrowIfNull(getHeaderValue);
        return new PayaffeWebhookHeaders(
            getHeaderValue(PayaffeWebhookHeaderNames.DeliveryId),
            getHeaderValue(PayaffeWebhookHeaderNames.Timestamp),
            getHeaderValue(PayaffeWebhookHeaderNames.Signature),
            getHeaderValue(PayaffeWebhookHeaderNames.EventType),
            getHeaderValue(PayaffeWebhookHeaderNames.EventVersion));
    }
}

public enum PayaffeWebhookRejectionReason
{
    MissingHeader,
    MalformedTimestamp,
    TimestampOutsideWindow,
    MalformedSignature,
    SignatureMismatch,
    MalformedPayload,
}

public sealed class PayaffeWebhookVerificationResult
{
    private PayaffeWebhookVerificationResult(
        PayaffeWebhookEvent? webhookEvent,
        PayaffeWebhookRejectionReason? rejectionReason,
        Guid? deliveryId)
    {
        Event = webhookEvent;
        RejectionReason = rejectionReason;
        DeliveryId = deliveryId;
    }

    public bool IsValid => RejectionReason is null;

    /// <summary>
    /// The verified envelope. It is available only after a successful verification, so an
    /// unverified payload can never be mistaken for a Payaffe statement about a Payment.
    /// </summary>
    public PayaffeWebhookEvent? Event { get; }

    public PayaffeWebhookRejectionReason? RejectionReason { get; }

    /// <summary>
    /// The Delivery identifier when the header carried a well-formed one. Deduplication uses
    /// the event identifier; this value is for correlating one delivery attempt in support.
    /// </summary>
    public Guid? DeliveryId { get; }

    internal static PayaffeWebhookVerificationResult Verified(
        PayaffeWebhookEvent webhookEvent,
        Guid? deliveryId) => new(webhookEvent, null, deliveryId);

    internal static PayaffeWebhookVerificationResult Rejected(
        PayaffeWebhookRejectionReason reason,
        Guid? deliveryId = null) => new(null, reason, deliveryId);
}

public sealed record PayaffeWebhookEvent(
    Guid EventId,
    string EventType,
    string EventVersion,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    PayaffeWebhookResource? Resource,
    PayaffeWebhookPayment Payment);

public sealed record PayaffeWebhookResource(string Type, Guid Id);

public sealed record PayaffeWebhookPayment(
    Guid PaymentId,
    string ExternalReference,
    PaymentStatus Status,
    string FiatCurrency,
    long FiatAmountMinor,
    SupportedCurrency? SelectedCurrency,
    string? ExpectedCryptoAmount,
    string? ExpectedCryptoAmountAtomic,
    string? ObservedTotal,
    string? ConfirmedEligibleTotal,
    string? ObservedAmountState,
    string? PayerPageId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? SettledAt);
