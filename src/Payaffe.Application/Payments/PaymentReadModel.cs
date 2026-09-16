namespace Payaffe.Application.Payments;

public sealed record PaymentReadModel(
    Guid Id,
    Guid IntegrationApiCredentialId,
    string ExternalReference,
    string FiatCurrency,
    long FiatAmountMinor,
    string Status,
    string PayerPageId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LateAcceptanceEndsAt,
    string? ContextUsername,
    string? ContextCustomerNumber,
    string? ContextCartName,
    string? ContextNote,
    string? ReturnUrl,
    string? SelectedCurrency,
    string? ExpectedCryptoAmount,
    string? PaymentAddress,
    string? ObservedTotal,
    string? ConfirmedEligibleTotal,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PaymentOptionReadModel>? PaymentOptions = null,
    DateTimeOffset? SettledAt = null,
    Guid ProjectId = default);

public sealed record PaymentOptionReadModel(
    string SupportedCurrency,
    string Status,
    string? UnavailableReason);
