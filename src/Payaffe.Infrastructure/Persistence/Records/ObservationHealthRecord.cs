namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class ObservationHealthRecord
{
    public string SupportedCurrency { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? LastSuccessfulAt { get; set; }

    public DateTimeOffset? LastFailedAt { get; set; }

    public string? LastSafeErrorCode { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }
}
