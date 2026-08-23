using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Payments;

/// <summary>
/// Excludes concurrent execution of the same named background worker across host
/// instances. A lease is held for a bounded duration so that a crashed owner
/// becomes recoverable once the lease expires.
/// </summary>
public sealed class BackgroundWorkerLeaseManager(PayaffeDbContext dbContext)
{
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);

    private readonly string _owner = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    public async Task<bool> TryAcquireAsync(
        string workerName,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return true;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into app.background_worker_leases
                (worker_name, consecutive_failure_count, updated_at, version)
            values ({workerName}, 0, {now}, 1)
            on conflict (worker_name) do nothing
            """,
            cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lease = await dbContext.BackgroundWorkerLeases
            .FromSqlInterpolated(
                $"select * from app.background_worker_leases where worker_name = {workerName} for update")
            .SingleAsync(cancellationToken);

        if (lease.LockedUntil > now && !StringComparer.Ordinal.Equals(lease.LockedBy, _owner))
        {
            await transaction.CommitAsync(cancellationToken);
            dbContext.Entry(lease).State = EntityState.Detached;
            return false;
        }

        lease.LockedBy = _owner;
        lease.LockedUntil = now.Add(leaseDuration > TimeSpan.Zero ? leaseDuration : DefaultLeaseDuration);
        lease.UpdatedAt = now;
        lease.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task CompleteAsync(string workerName, DateTimeOffset now, CancellationToken cancellationToken) =>
        ReleaseAsync(workerName, now, succeeded: true, safeErrorCode: null, cancellationToken);

    public Task FailAsync(
        string workerName,
        DateTimeOffset now,
        string safeErrorCode,
        CancellationToken cancellationToken) =>
        ReleaseAsync(workerName, now, succeeded: false, safeErrorCode, cancellationToken);

    private async Task ReleaseAsync(
        string workerName,
        DateTimeOffset now,
        bool succeeded,
        string? safeErrorCode,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        var lease = await dbContext.BackgroundWorkerLeases
            .SingleAsync(record => record.WorkerName == workerName, cancellationToken);
        if (!StringComparer.Ordinal.Equals(lease.LockedBy, _owner))
        {
            return;
        }

        lease.LockedBy = null;
        lease.LockedUntil = null;
        lease.LastSucceededAt = succeeded ? now : lease.LastSucceededAt;
        lease.LastFailedAt = succeeded ? lease.LastFailedAt : now;
        lease.LastSafeErrorCode = safeErrorCode;
        lease.ConsecutiveFailureCount = succeeded ? 0 : lease.ConsecutiveFailureCount + 1;
        lease.UpdatedAt = now;
        lease.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
