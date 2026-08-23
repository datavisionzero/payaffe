namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class AdminRecoveryCodeRecord
{
    public Guid Id { get; set; }

    public Guid AdminAccountId { get; set; }

    public string CodeHash { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public long Version { get; set; } = 1;
}
