namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class PaymentRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid IntegrationApiCredentialId { get; set; }

    public string ExternalReference { get; set; } = string.Empty;

    public string FiatCurrency { get; set; } = string.Empty;

    public long FiatAmountMinor { get; set; }

    public string Status { get; set; } = string.Empty;

    public string PayerPageId { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset LateAcceptanceEndsAt { get; set; }

    public string? ContextUsername { get; set; }

    public string? ContextCustomerNumber { get; set; }

    public string? ContextCartName { get; set; }

    public string? ContextNote { get; set; }

    public string? ReturnUrl { get; set; }

    public string? SelectedCurrency { get; set; }

    public string? ExpectedCryptoAmount { get; set; }

    public string? PaymentAddress { get; set; }

    public string? ConfirmedEligibleTotal { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
