namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class NativeEthAddressPoolImportRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid ImportedByAdminAccountId { get; set; }

    public int AddressCount { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
}
