using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Worker;

/// <summary>
/// Refreshes the heartbeat that the container healthcheck reads, but only
/// once the schema is in place, while this process can still reach the
/// product database, and while none of its scheduled worker loops has died.
/// </summary>
/// <remarks>
/// The heartbeat is deliberately per-process rather than derived from
/// `app.background_worker_leases`. Lease rows are database-wide, so a second,
/// healthy instance would keep them fresh and make a wedged instance look
/// healthy. Whether the scheduled work is actually progressing is an alerting
/// question, not a liveness question; see the worker alert rules in
/// `deploy/observability/prometheus-alerts.yaml`.
/// </remarks>
public sealed class WorkerHealthHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerHealthOptions> options,
    ILogger<WorkerHealthHostedService> logger,
    SchemaMigrationState schemaMigration,
    IServiceProvider serviceProvider) : BackgroundService
{
    private readonly WorkerHealthOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The migration runs in the background (ADR 0027), so the host starts
        // before it has finished. A heartbeat written before the workers can
        // run would report a host healthy that has not done anything yet, and
        // one whose migration failed would never have been healthy at all.
        try
        {
            await schemaMigration.Completion.WaitAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Worker host has no heartbeat because its schema migration failed.");
            return;
        }

        // Resolved here rather than injected: this service is one of them.
        var workerLoops = serviceProvider.GetServices<IHostedService>()
            .OfType<SchemaGatedBackgroundService>()
            .ToArray();
        var heartbeat = new WorkerHeartbeatFile(_options.HeartbeatPath);
        using var timer = new PeriodicTimer(_options.ProbeInterval);
        do
        {
            try
            {
                if (workerLoops.FirstOrDefault(loop => loop.ExecuteTask is { IsFaulted: true }) is { } failed)
                {
                    // Left to go stale: a worker loop that died does not come
                    // back, so the container has to be restarted.
                    logger.LogWarning("Worker loop {WorkerLoop} has stopped with a failure.", failed.GetType().Name);
                    continue;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
                if (await dbContext.Database.CanConnectAsync(stoppingToken))
                {
                    await heartbeat.WriteAsync(DateTimeOffset.UtcNow, stoppingToken);
                }
                else
                {
                    // The heartbeat is left to go stale instead of being
                    // deleted, so a transient blip does not immediately flip
                    // the container to unhealthy.
                    logger.LogWarning("Worker host cannot reach the product database.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Worker health probe failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // A stopped host must not keep reporting healthy if the container is
        // restarted into a state where the loop never starts again.
        try
        {
            File.Delete(_options.HeartbeatPath);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Worker host could not remove its heartbeat file.");
        }
    }
}
