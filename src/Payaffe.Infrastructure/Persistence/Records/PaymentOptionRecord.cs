namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class PaymentOptionRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid PaymentId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? UnavailableReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
