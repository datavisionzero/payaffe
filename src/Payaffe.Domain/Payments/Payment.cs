namespace Payaffe.Domain.Payments;

public sealed class Payment
{
    private readonly List<PaymentEvent> _events;

    private Payment(
        Guid id,
        Guid integrationApiCredentialId,
        ExternalReference externalReference,
        FiatAmount fiatAmount,
        PaymentContextFields paymentContext,
        Uri? returnUrl,
        string payerPageId,
        DateTimeOffset expiresAt,
        DateTimeOffset lateAcceptanceEndsAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        IntegrationApiCredentialId = integrationApiCredentialId;
        ExternalReference = externalReference;
        FiatAmount = fiatAmount;
        Status = PaymentStatus.PendingCurrencySelection;
        PaymentContext = paymentContext;
        ReturnUrl = returnUrl;
        PayerPageId = payerPageId;
        ExpiresAt = expiresAt;
        LateAcceptanceEndsAt = lateAcceptanceEndsAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;

        _events =
        [
            new PaymentCreatedEvent(Guid.NewGuid(), id, createdAt),
        ];
    }

    public Guid Id { get; }

    public Guid IntegrationApiCredentialId { get; }

    public ExternalReference ExternalReference { get; }

    public FiatAmount FiatAmount { get; }

    public PaymentStatus Status { get; }

    public PaymentContextFields PaymentContext { get; }

    public Uri? ReturnUrl { get; }

    public string PayerPageId { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset LateAcceptanceEndsAt { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public IReadOnlyCollection<PaymentEvent> Events => _events.AsReadOnly();

    public static Payment Create(
        Guid integrationApiCredentialId,
        ExternalReference externalReference,
        FiatAmount fiatAmount,
        PaymentContextFields paymentContext,
        Uri? returnUrl,
        string payerPageId,
        DateTimeOffset createdAt,
        TimeSpan paymentExpiration,
        TimeSpan lateAcceptanceWindow)
    {
        if (integrationApiCredentialId == Guid.Empty)
        {
            throw new DomainRuleException(
                "Integration API Credential identifier is required.",
                "integration_api_credential_id.required");
        }

        if (string.IsNullOrWhiteSpace(payerPageId))
        {
            throw new DomainRuleException("Payer Page identifier is required.", "payer_page_id.required");
        }

        var trimmedPayerPageId = payerPageId.Trim();
        if (trimmedPayerPageId.Length > 255)
        {
            throw new DomainRuleException(
                "Payer Page identifier must be at most 255 characters.",
                "payer_page_id.too_long");
        }

        if (paymentExpiration <= TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "Payment Expiration must be greater than zero.",
                "payment_expiration.not_positive");
        }

        if (lateAcceptanceWindow < TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "Late Acceptance Window must not be negative.",
                "late_acceptance_window.negative");
        }

        return new Payment(
            Guid.NewGuid(),
            integrationApiCredentialId,
            externalReference,
            fiatAmount,
            paymentContext,
            returnUrl,
            trimmedPayerPageId,
            createdAt.Add(paymentExpiration),
            createdAt.Add(paymentExpiration).Add(lateAcceptanceWindow),
            createdAt);
    }
}
