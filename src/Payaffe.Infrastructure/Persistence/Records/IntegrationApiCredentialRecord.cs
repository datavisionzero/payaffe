namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class IntegrationApiCredentialRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
