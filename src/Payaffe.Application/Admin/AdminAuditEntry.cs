namespace Payaffe.Application.Admin;

public sealed record AdminAuditEntry(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string EventType,
    string Outcome,
    string ActorType,
    string ActorId,
    string SourceService,
    string? SourceIp,
    string? UserAgent,
    string CorrelationId,
    string ReasonCode,
    string SubjectType,
    string SubjectId,
    Guid? ProjectId = null);
