namespace Payaffe.Application.Payments;

public sealed record PaymentResponse(
    Guid PaymentId,
    string Status,
    string PayerPageUrl,
    DateTimeOffset ExpiresAt,
    string FiatCurrency,
    long FiatAmountMinor,
    string ExternalReference,
    string? SelectedCurrency,
    string? ExpectedCryptoAmount,
    string? PaymentAddress,
    string? ObservedTotal,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? SettledAt,
    string? ReturnUrl,
    IReadOnlyList<PaymentOptionResponse> PaymentOptions);

public sealed record PaymentOptionResponse(
    string SupportedCurrency,
    string Status,
    string? UnavailableReasonCode);
