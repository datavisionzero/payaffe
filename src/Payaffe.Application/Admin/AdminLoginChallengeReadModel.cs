namespace Payaffe.Application.Admin;

public sealed record AdminLoginChallengeReadModel(
    Guid Id,
    Guid AdminAccountId,
    string TotpSecretReference,
    string AdminAccountStatus,
    DateTimeOffset ExpiresAt,
    int FailedAttemptCount,
    DateTimeOffset? ConsumedAt);
