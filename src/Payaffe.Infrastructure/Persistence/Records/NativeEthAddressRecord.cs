namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class NativeEthAddressRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid ImportId { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public Guid? AssignedPaymentId { get; set; }

    public DateTimeOffset? AssignedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }
}
