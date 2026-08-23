namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class RateLockRecord
{
    public Guid PaymentId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string FiatCurrency { get; set; } = string.Empty;

    public long FiatAmountMinor { get; set; }

    public string ExpectedCryptoAmount { get; set; } = string.Empty;

    public string RateSource { get; set; } = string.Empty;

    public string RateValue { get; set; } = string.Empty;

    public DateTimeOffset RateObservedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
