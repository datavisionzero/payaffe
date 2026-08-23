namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class WatchOnlyWalletCursorRecord
{
    public string SupportedCurrency { get; set; } = string.Empty;

    public string SourceFingerprint { get; set; } = string.Empty;

    public long NextDerivationIndex { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }
}
