namespace Payaffe.Application.Admin;

public sealed record AdminIntegrationApiCredentialDraft(
    Guid Id,
    string Name,
    string TokenHash,
    DateTimeOffset CreatedAt);
