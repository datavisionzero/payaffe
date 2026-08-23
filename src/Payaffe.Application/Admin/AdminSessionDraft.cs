namespace Payaffe.Application.Admin;

public sealed record AdminSessionDraft(
    Guid Id,
    Guid AdminAccountId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset? MfaAuthenticatedAt,

    /// <summary>
    /// Null for a session that signed in on a password alone: the account has
    /// no second factor, so there is nothing for a freshness window to be
    /// measured against (ADR 0028).
    /// </summary>
    DateTimeOffset? StepUpAuthenticatedAt);
