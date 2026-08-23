namespace Payaffe.Application.Admin;

public sealed record AdminSessionDraft(
    Guid Id,
    Guid AdminAccountId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset MfaAuthenticatedAt,
    DateTimeOffset StepUpAuthenticatedAt);
