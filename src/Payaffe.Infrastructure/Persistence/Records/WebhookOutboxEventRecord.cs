namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class WebhookOutboxEventRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public Guid IntegrationApiCredentialId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string EventVersion { get; set; } = string.Empty;

    public int PayloadVersion { get; set; }

    public string ResourceType { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public string? LastErrorCode { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}
