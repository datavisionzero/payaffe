namespace Payaffe.Application.Admin;

public sealed record AdminPaymentDetailReadModel(
    Guid ProjectId,
    Guid PaymentId,
    string ExternalReference,
    string FiatCurrency,
    long FiatAmountMinor,
    string Status,
    string PayerPageId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LateAcceptanceEndsAt,
    string? SelectedCurrency,
    string? ExpectedCryptoAmount,
    string? PaymentAddress,
    string? ObservedTotal,
    string? ConfirmedEligibleTotal,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SettledAt,
    long Version);
