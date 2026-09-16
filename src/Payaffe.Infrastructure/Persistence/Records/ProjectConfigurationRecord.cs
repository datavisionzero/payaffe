namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class ProjectConfigurationRecord
{
    public Guid ProjectId { get; set; }

    public long PaymentExpirationSeconds { get; set; }

    public long LateAcceptanceWindowSeconds { get; set; }

    public decimal PaymentTolerancePercent { get; set; }

    public int BtcConfirmationRequirement { get; set; }

    public int LtcConfirmationRequirement { get; set; }

    public int EthConfirmationRequirement { get; set; }

    public int BtcReorgMonitoringDepth { get; set; }

    public int LtcReorgMonitoringDepth { get; set; }

    public int EthReorgMonitoringDepth { get; set; }

    public int NativeEthLowCapacityThreshold { get; set; }

    public string LegacySettingsFingerprint { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
