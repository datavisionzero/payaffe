using Payaffe.Application;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Persistence;

public sealed class BackgroundWorkerLeaseTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string WorkerName = "test-worker";

    [Fact]
    public async Task Active_lease_excludes_a_second_host_instance()
    {
        await using var context = await BuildContextAsync();
        var now = DateTimeOffset.UtcNow;

        Assert.True(await context.First.TryAcquireAsync(WorkerName, now, TimeSpan.FromMinutes(2), CancellationToken.None));
        Assert.False(await context.Second.TryAcquireAsync(WorkerName, now.AddSeconds(1), TimeSpan.FromMinutes(2), CancellationToken.None));
    }

    [Fact]
    public async Task Failed_run_releases_the_lease_and_records_the_safe_error_code()
    {
        await using var context = await BuildContextAsync();
        var now = DateTimeOffset.UtcNow;

        await context.First.TryAcquireAsync(WorkerName, now, TimeSpan.FromMinutes(2), CancellationToken.None);
        await context.First.FailAsync(WorkerName, now.AddSeconds(1), "worker.batch_failed", CancellationToken.None);

        var lease = await context.ReadLeaseAsync();
        Assert.Null(lease.LockedBy);
        Assert.Null(lease.LockedUntil);
        Assert.Equal("worker.batch_failed", lease.LastSafeErrorCode);
        Assert.Equal(1, lease.ConsecutiveFailureCount);
        Assert.NotNull(lease.LastFailedAt);
        Assert.Null(lease.LastSucceededAt);
    }

    [Fact]
    public async Task Released_lease_can_be_taken_over_and_success_resets_the_failure_count()
    {
        await using var context = await BuildContextAsync();
        var now = DateTimeOffset.UtcNow;

        await context.First.TryAcquireAsync(WorkerName, now, TimeSpan.FromMinutes(2), CancellationToken.None);
        await context.First.FailAsync(WorkerName, now.AddSeconds(1), "worker.batch_failed", CancellationToken.None);

        Assert.True(await context.Second.TryAcquireAsync(WorkerName, now.AddSeconds(2), TimeSpan.FromMinutes(2), CancellationToken.None));
        await context.Second.CompleteAsync(WorkerName, now.AddSeconds(3), CancellationToken.None);

        var lease = await context.ReadLeaseAsync();
        Assert.Null(lease.LockedBy);
        Assert.Equal(0, lease.ConsecutiveFailureCount);
        Assert.Null(lease.LastSafeErrorCode);
        Assert.NotNull(lease.LastSucceededAt);
        Assert.NotNull(lease.LastFailedAt);
    }

    [Fact]
    public async Task Expired_lease_of_a_crashed_owner_becomes_claimable_again()
    {
        await using var context = await BuildContextAsync();
        var now = DateTimeOffset.UtcNow;

        // The first instance acquires a short lease and never releases it.
        Assert.True(await context.First.TryAcquireAsync(WorkerName, now, TimeSpan.FromMinutes(2), CancellationToken.None));

        Assert.False(await context.Second.TryAcquireAsync(WorkerName, now.AddMinutes(1), TimeSpan.FromMinutes(2), CancellationToken.None));
        Assert.True(await context.Second.TryAcquireAsync(WorkerName, now.AddMinutes(3), TimeSpan.FromMinutes(2), CancellationToken.None));
    }

    [Fact]
    public async Task Consecutive_failures_are_counted_for_operator_diagnosis()
    {
        await using var context = await BuildContextAsync();
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.True(await context.First.TryAcquireAsync(WorkerName, now, TimeSpan.FromMinutes(2), CancellationToken.None));
            await context.First.FailAsync(WorkerName, now, "worker.batch_failed", CancellationToken.None);
            now = now.AddSeconds(1);
        }

        var lease = await context.ReadLeaseAsync();
        Assert.Equal(3, lease.ConsecutiveFailureCount);
    }

    private async Task<LeaseContext> BuildContextAsync()
    {
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(await postgres.CreateDatabaseAsync());
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);

        var firstScope = serviceProvider.CreateAsyncScope();
        var secondScope = serviceProvider.CreateAsyncScope();

        return new LeaseContext(
            serviceProvider,
            firstScope,
            secondScope,
            firstScope.ServiceProvider.GetRequiredService<BackgroundWorkerLeaseManager>(),
            secondScope.ServiceProvider.GetRequiredService<BackgroundWorkerLeaseManager>());
    }

    private sealed record LeaseContext(
        ServiceProvider ServiceProvider,
        AsyncServiceScope FirstScope,
        AsyncServiceScope SecondScope,
        BackgroundWorkerLeaseManager First,
        BackgroundWorkerLeaseManager Second) : IAsyncDisposable
    {
        public async Task<BackgroundWorkerLeaseRecord> ReadLeaseAsync()
        {
            await using var scope = ServiceProvider.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<PayaffeDbContext>()
                .BackgroundWorkerLeases
                .AsNoTracking()
                .SingleAsync(lease => lease.WorkerName == WorkerName);
        }

        public async ValueTask DisposeAsync()
        {
            await FirstScope.DisposeAsync();
            await SecondScope.DisposeAsync();
            await ServiceProvider.DisposeAsync();
        }
    }
}
