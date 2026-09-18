namespace Payaffe.Application.Admin;

public sealed record AdminAuditLogEntryReadModel(
    Guid? ProjectId,
    Guid EventId,
    DateTimeOffset OccurredAt,
    string EventType,
    string Outcome,
    string ActorType,
    string ActorId,
    string ReasonCode,
    string SubjectType,
    string SubjectId);
