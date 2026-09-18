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
    Guid ProjectId = default,
    RateLockReadModel? RateLock = null,
    PaymentInstructionReadModel? PaymentInstruction = null);

public sealed record PaymentOptionReadModel(
    string SupportedCurrency,
    string Status,
    string? UnavailableReason,
    DateTimeOffset CheckedAt = default);

public sealed record RateLockReadModel(
    string FiatCurrency,
    long FiatAmountMinor,
    string SupportedCurrency,
    string ExpectedCryptoAmount,
    string FiatPerCryptoUnit,
    string Source,
    DateTimeOffset RateObservedAt,
    DateTimeOffset LockedAt);

public sealed record PaymentInstructionReadModel(
    string SupportedCurrency,
    string Network,
    long? ChainId,
    string Amount,
    string PaymentAddress);
