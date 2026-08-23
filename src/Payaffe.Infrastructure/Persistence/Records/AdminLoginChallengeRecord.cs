namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class AdminLoginChallengeRecord
{
    public Guid Id { get; set; }

    public Guid AdminAccountId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int FailedAttemptCount { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long Version { get; set; } = 1;
}
