namespace Payaffe.Application.Admin;

public sealed record AdminSessionReadModel(
    Guid Id,
    Guid AdminAccountId,
    string Username,
    string AdminAccountStatus,
    string TotpSecretReference,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset? MfaAuthenticatedAt,
    DateTimeOffset? StepUpAuthenticatedAt,
    DateTimeOffset? RevokedAt);
