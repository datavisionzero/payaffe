namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class PaymentAddressAssignmentRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid PaymentId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string PaymentAddress { get; set; } = string.Empty;

    public string Network { get; set; } = "mainnet";

    public long? ChainId { get; set; }

    public string? SourceFingerprint { get; set; }

    public long? DerivationIndex { get; set; }

    public DateTimeOffset AssignedAt { get; set; }
}
