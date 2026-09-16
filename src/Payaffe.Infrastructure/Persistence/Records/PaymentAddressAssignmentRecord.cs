namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class PaymentAddressAssignmentRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid PaymentId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string PaymentAddress { get; set; } = string.Empty;

    public DateTimeOffset AssignedAt { get; set; }
}
