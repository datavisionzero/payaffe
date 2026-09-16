namespace Payaffe.Application.Admin;

public sealed record AdminIntegrationApiCredentialReadModel(
    Guid ProjectId,
    Guid Id,
    string Name,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset UpdatedAt,
    long Version);
