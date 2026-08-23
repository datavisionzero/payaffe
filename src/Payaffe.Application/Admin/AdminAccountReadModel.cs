namespace Payaffe.Application.Admin;

public sealed record AdminAccountReadModel(
    Guid Id,
    string Username,
    string NormalizedUsername,
    string PasswordHash,
    string? TotpSecretReference,
    string Status,
    int FailedPasswordAttemptCount,
    DateTimeOffset? LockedUntil);
