namespace Payaffe.Application.Admin;

public sealed record AdminWebhookDeliveryReadModel(
    Guid ProjectId,
    Guid WebhookEventId,
    Guid PaymentId,
    string PaymentExternalReference,
    string EventType,
    string EventVersion,
    string Status,
    int AttemptCount,
    string? LastErrorCode,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptedAt,
    string? LastAttemptResult,
    int? LastHttpStatusCode,
    string? LastSafeErrorCode,
    string CorrelationId);
