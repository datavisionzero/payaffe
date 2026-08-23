namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class BackgroundWorkerLeaseRecord
{
    public string WorkerName { get; set; } = string.Empty;

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastSucceededAt { get; set; }

    public DateTimeOffset? LastFailedAt { get; set; }

    public string? LastSafeErrorCode { get; set; }

    public int ConsecutiveFailureCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
