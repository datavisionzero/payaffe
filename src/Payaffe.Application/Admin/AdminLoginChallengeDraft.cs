namespace Payaffe.Application.Admin;

public sealed record AdminLoginChallengeDraft(
    Guid Id,
    Guid AdminAccountId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);
