namespace Payaffe.Application.Admin;

public sealed record AdminWebhookEndpointReadModel(
    Guid ProjectId,
    Guid Id,
    Guid IntegrationApiCredentialId,
    string Url,
    string SecretReference,
    string Status,
    IReadOnlyList<string> EventTypes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record AdminWebhookEndpointDraft(
    Guid Id,
    Guid IntegrationApiCredentialId,
    string Url,
    string SecretReference,
    string? EventTypes,
    DateTimeOffset CreatedAt);

public sealed record AdminWebhookEndpointUpdate(
    string Url,
    string? EventTypes);
