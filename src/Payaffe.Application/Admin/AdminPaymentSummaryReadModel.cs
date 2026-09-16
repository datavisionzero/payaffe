namespace Payaffe.Application.Admin;

public sealed record AdminPaymentSummaryReadModel(
    Guid ProjectId,
    Guid PaymentId,
    string ExternalReference,
    string FiatCurrency,
    long FiatAmountMinor,
    string Status,
    string? SelectedCurrency,
    string? ExpectedCryptoAmount,
    string? PaymentAddress,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
