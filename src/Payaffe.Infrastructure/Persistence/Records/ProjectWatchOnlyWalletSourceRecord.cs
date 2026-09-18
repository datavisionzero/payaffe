namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class ProjectWatchOnlyWalletSourceRecord
{
    public Guid ProjectId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string SourceFingerprint { get; set; } = string.Empty;

    public string Network { get; set; } = string.Empty;

    public string AddressType { get; set; } = string.Empty;

    public long StartingIndex { get; set; }

    public string SourceReference { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
