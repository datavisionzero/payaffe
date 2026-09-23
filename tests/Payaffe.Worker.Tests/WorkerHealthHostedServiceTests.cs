using Payaffe.Infrastructure.Persistence;
using Payaffe.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Payaffe.Worker.Tests;

/// <summary>
/// The heartbeat is what the container healthcheck judges the worker by, so it
/// must not claim a host is healthy before its workers can run or after one of
/// them has died.
/// </summary>
public sealed class WorkerHealthHostedServiceTests : IDisposable
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(20);

    private readonly string _heartbeatPath = Path.Combine(
        Path.GetTempPath(),
        $"payaffe-worker-health-tests-{Guid.NewGuid():N}",
        "health");

    [Fact]
    public async Task No_heartbeat_is_written_while_the_schema_migration_is_still_running()
    {
        using var provider = BuildProvider(new SchemaMigrationState());
        var health = CreateHealthService(provider);

        await health.StartAsync(CancellationToken.None);
        await Task.Delay(ProbeInterval * 10);

        Assert.False(File.Exists(_heartbeatPath));
        await health.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_heartbeat_is_written_once_the_schema_is_in_place()
    {
        using var provider = BuildProvider(SchemaMigrationState.AlreadyApplied());
        var health = CreateHealthService(provider);

        await health.StartAsync(CancellationToken.None);
        await WaitForAsync(() => File.Exists(_heartbeatPath));

        await health.StopAsync(CancellationToken.None);
        Assert.False(File.Exists(_heartbeatPath));
    }

    [Fact]
    public async Task No_heartbeat_is_written_after_a_worker_loop_died()
    {
        using var provider = BuildProvider(SchemaMigrationState.AlreadyApplied(), withFailingLoop: true);
        var failingLoop = provider.GetServices<IHostedService>().OfType<FailingWorkerLoop>().Single();
        await failingLoop.StartAsync(CancellationToken.None);
        await WaitForAsync(() => failingLoop.ExecuteTask is { IsFaulted: true });
        var health = CreateHealthService(provider);

        await health.StartAsync(CancellationToken.None);
        await Task.Delay(ProbeInterval * 10);

        Assert.False(File.Exists(_heartbeatPath));
        await health.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_heartbeatPath)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private WorkerHealthHostedService CreateHealthService(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WorkerHealthOptions { HeartbeatPath = _heartbeatPath, ProbeInterval = ProbeInterval }),
            NullLogger<WorkerHealthHostedService>.Instance,
            provider.GetRequiredService<SchemaMigrationState>(),
            provider);

    private static ServiceProvider BuildProvider(SchemaMigrationState schemaMigration, bool withFailingLoop = false)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<PayaffeDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(schemaMigration);
        if (withFailingLoop)
        {
            services.AddHostedService<FailingWorkerLoop>();
        }

        return services.BuildServiceProvider();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline && !condition())
        {
            await Task.Delay(ProbeInterval);
        }

        Assert.True(condition());
    }

    private sealed class FailingWorkerLoop(SchemaMigrationState schemaMigration)
        : SchemaGatedBackgroundService(schemaMigration)
    {
        protected override async Task RunAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("The worker loop died.");
        }
    }
}
