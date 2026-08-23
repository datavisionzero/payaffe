namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class AdminAccountRecord
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string NormalizedUsername { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string? TotpSecretReference { get; set; }

    public string Status { get; set; } = string.Empty;

    public int FailedPasswordAttemptCount { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastPasswordVerifiedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
