using Payaffe.Domain.Payments;

namespace Payaffe.Domain.Tests.Payments;

public sealed class PaymentTests
{
    [Fact]
    public void Create_initializes_pending_currency_selection_payment()
    {
        var createdAt = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
        var credentialId = Guid.NewGuid();
        var externalReference = ExternalReference.Create("order-123");
        var fiatAmount = FiatAmount.Create("EUR", 1999);
        var context = PaymentContextFields.Create(username: "customer@example.test");
        var returnUrl = new Uri("https://example.test/orders/order-123");

        var payment = Payment.Create(
            credentialId,
            externalReference,
            fiatAmount,
            context,
            returnUrl,
            "abc123",
            createdAt,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24));

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(credentialId, payment.IntegrationApiCredentialId);
        Assert.Same(externalReference, payment.ExternalReference);
        Assert.Same(fiatAmount, payment.FiatAmount);
        Assert.Same(context, payment.PaymentContext);
        Assert.Equal(returnUrl, payment.ReturnUrl);
        Assert.Equal("abc123", payment.PayerPageId);
        Assert.Equal(PaymentStatus.PendingCurrencySelection, payment.Status);
        Assert.Equal("pending_currency_selection", payment.Status.Value);
        Assert.Equal(createdAt, payment.CreatedAt);
        Assert.Equal(createdAt, payment.UpdatedAt);
        Assert.Equal(createdAt.AddHours(1), payment.ExpiresAt);
        Assert.Equal(createdAt.AddHours(25), payment.LateAcceptanceEndsAt);
    }

    [Fact]
    public void Create_records_payment_created_event()
    {
        var createdAt = DateTimeOffset.Parse("2026-07-04T12:00:00Z");

        var payment = CreatePayment(createdAt);

        var paymentCreated = Assert.Single(payment.Events);
        Assert.IsType<PaymentCreatedEvent>(paymentCreated);
        Assert.NotEqual(Guid.Empty, paymentCreated.Id);
        Assert.Equal(payment.Id, paymentCreated.PaymentId);
        Assert.Equal("payment.created", paymentCreated.EventType);
        Assert.Equal(createdAt, paymentCreated.OccurredAt);
    }

    [Fact]
    public void Create_requires_integration_api_credential_id()
    {
        var exception = Assert.Throws<DomainRuleException>(() => Payment.Create(
            Guid.Empty,
            ExternalReference.Create("order-123"),
            FiatAmount.Create("EUR", 1999),
            PaymentContextFields.Empty,
            returnUrl: null,
            payerPageId: "abc123",
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24)));

        Assert.Equal("integration_api_credential_id.required", exception.Code);
    }

    [Fact]
    public void Create_requires_payer_page_id()
    {
        var exception = Assert.Throws<DomainRuleException>(() => Payment.Create(
            Guid.NewGuid(),
            ExternalReference.Create("order-123"),
            FiatAmount.Create("EUR", 1999),
            PaymentContextFields.Empty,
            returnUrl: null,
            payerPageId: " ",
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24)));

        Assert.Equal("payer_page_id.required", exception.Code);
    }

    [Fact]
    public void Create_requires_positive_payment_expiration()
    {
        var exception = Assert.Throws<DomainRuleException>(() => Payment.Create(
            Guid.NewGuid(),
            ExternalReference.Create("order-123"),
            FiatAmount.Create("EUR", 1999),
            PaymentContextFields.Empty,
            returnUrl: null,
            payerPageId: "abc123",
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            TimeSpan.FromHours(24)));

        Assert.Equal("payment_expiration.not_positive", exception.Code);
    }

    [Fact]
    public void Create_rejects_negative_late_acceptance_window()
    {
        var exception = Assert.Throws<DomainRuleException>(() => Payment.Create(
            Guid.NewGuid(),
            ExternalReference.Create("order-123"),
            FiatAmount.Create("EUR", 1999),
            PaymentContextFields.Empty,
            returnUrl: null,
            payerPageId: "abc123",
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            TimeSpan.FromTicks(-1)));

        Assert.Equal("late_acceptance_window.negative", exception.Code);
    }

    private static Payment CreatePayment(DateTimeOffset createdAt)
    {
        return Payment.Create(
            Guid.NewGuid(),
            ExternalReference.Create("order-123"),
            FiatAmount.Create("EUR", 1999),
            PaymentContextFields.Empty,
            returnUrl: null,
            payerPageId: "abc123",
            createdAt,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24));
    }
}
