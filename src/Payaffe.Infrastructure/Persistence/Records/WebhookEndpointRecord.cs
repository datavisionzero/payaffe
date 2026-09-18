namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class WebhookEndpointRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid IntegrationApiCredentialId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string SecretReference { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? EventTypes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
