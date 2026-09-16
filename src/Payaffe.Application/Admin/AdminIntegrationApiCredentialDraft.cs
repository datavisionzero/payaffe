namespace Payaffe.Application.Admin;

public sealed record AdminIntegrationApiCredentialDraft(
    Guid ProjectId,
    Guid Id,
    string Name,
    string TokenHash,
    DateTimeOffset CreatedAt);
