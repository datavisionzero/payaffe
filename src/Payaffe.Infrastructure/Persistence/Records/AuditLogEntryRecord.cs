namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class AuditLogEntryRecord
{
    public Guid EventId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string ActorType { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string SourceService { get; set; } = string.Empty;

    public string? SourceIp { get; set; }

    public string? UserAgent { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public string SubjectType { get; set; } = string.Empty;

    public string SubjectId { get; set; } = string.Empty;
}
