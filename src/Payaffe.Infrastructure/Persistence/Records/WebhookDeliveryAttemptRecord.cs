namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class WebhookDeliveryAttemptRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid WebhookEventId { get; set; }

    public Guid WebhookEndpointId { get; set; }

    public int AttemptNumber { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }

    public string Result { get; set; } = string.Empty;

    public int? HttpStatusCode { get; set; }

    public string? SafeErrorCode { get; set; }

    public DateTimeOffset? NextRetryAt { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}
