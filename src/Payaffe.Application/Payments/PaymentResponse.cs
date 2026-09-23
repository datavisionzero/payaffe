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
    IReadOnlyList<PaymentOptionResponse> PaymentOptions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LateAcceptanceEndsAt,
    string? ConfirmedEligibleTotal,
    string? ObservedAmountState,
    RateLockResponse? RateLock,
    PaymentInstructionResponse? PaymentInstruction,
    bool TestMode = false);

public sealed record PaymentOptionResponse(
    string SupportedCurrency,
    string Status,
    string? UnavailableReasonCode,
    DateTimeOffset CheckedAt);

public sealed record RateLockResponse(
    string FiatCurrency,
    long FiatAmountMinor,
    string SupportedCurrency,
    string ExpectedCryptoAmount,
    string ExpectedCryptoAmountAtomic,
    string FiatPerCryptoUnit,
    string Source,
    DateTimeOffset RateObservedAt,
    DateTimeOffset LockedAt,
    DateTimeOffset ValidUntil);

public sealed record PaymentInstructionResponse(
    string SupportedCurrency,
    string Network,
    long? ChainId,
    string Amount,
    string AmountAtomic,
    string PaymentAddress,
    string Uri,
    DateTimeOffset ExpiresAt);
