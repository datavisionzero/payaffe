namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class AdminSessionRecord
{
    public Guid Id { get; set; }

    public Guid AdminAccountId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset IdleExpiresAt { get; set; }

    public DateTimeOffset? MfaAuthenticatedAt { get; set; }

    public DateTimeOffset? StepUpAuthenticatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public long Version { get; set; } = 1;
}
