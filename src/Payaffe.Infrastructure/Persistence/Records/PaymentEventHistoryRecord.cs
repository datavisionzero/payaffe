namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class PaymentEventHistoryRecord
{
    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string? Details { get; set; }
}
