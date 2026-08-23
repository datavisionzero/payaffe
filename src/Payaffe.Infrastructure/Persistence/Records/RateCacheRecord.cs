namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class RateCacheRecord
{
    public string FiatCurrency { get; set; } = string.Empty;

    public string SupportedCurrency { get; set; } = string.Empty;

    public string RateSource { get; set; } = string.Empty;

    public string RateValue { get; set; } = string.Empty;

    public DateTimeOffset ObservedAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public long Version { get; set; } = 1;
}
