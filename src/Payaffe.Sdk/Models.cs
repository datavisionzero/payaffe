namespace Payaffe.Sdk;

public sealed record CreatePaymentRequest(
    string FiatCurrency,
    long FiatAmountMinor,
    string ExternalReference,
    PaymentContext? PaymentContext = null,
    string? ReturnUrl = null);

public sealed record PaymentContext(
    string? Username = null,
    string? CustomerNumber = null,
    string? CartName = null,
    string? Note = null);

public sealed record Payment(
    Guid PaymentId,
    PaymentStatus Status,
    string PayerPageUrl,
    DateTimeOffset ExpiresAt,
    string FiatCurrency,
    long FiatAmountMinor,
    string ExternalReference,
    SupportedCurrency? SelectedCurrency,
    string? ExpectedCryptoAmount,
    string? PaymentAddress,
    string? ObservedTotal,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? SettledAt,
    string? ReturnUrl,
    IReadOnlyList<PaymentOption> PaymentOptions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LateAcceptanceEndsAt,
    string? ConfirmedEligibleTotal,
    string? ObservedAmountState,
    RateLock? RateLock,
    PaymentInstruction? PaymentInstruction);

public sealed record PaymentOption(
    SupportedCurrency SupportedCurrency,
    PaymentOptionStatus Status,
    string? UnavailableReasonCode,
    DateTimeOffset CheckedAt);

public sealed record RateLock(
    string FiatCurrency,
    long FiatAmountMinor,
    SupportedCurrency SupportedCurrency,
    string ExpectedCryptoAmount,
    string ExpectedCryptoAmountAtomic,
    string FiatPerCryptoUnit,
    string Source,
    DateTimeOffset RateObservedAt,
    DateTimeOffset LockedAt,
    DateTimeOffset ValidUntil);

public sealed record PaymentInstruction(
    SupportedCurrency SupportedCurrency,
    string Network,
    long? ChainId,
    string Amount,
    string AmountAtomic,
    string PaymentAddress,
    string Uri,
    DateTimeOffset ExpiresAt);

public sealed class PaymentPollingOptions
{
    public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaximumInterval { get; set; } = TimeSpan.FromSeconds(15);

    public double JitterRatio { get; set; } = 0.2;

    internal void Validate()
    {
        if (InitialInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitialInterval),
                "The polling interval must be greater than zero.");
        }

        if (MaximumInterval < InitialInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumInterval),
                "The maximum polling interval must not be shorter than the initial interval.");
        }

        if (JitterRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(JitterRatio),
                "The polling jitter ratio must be between zero and one.");
        }
    }
}
