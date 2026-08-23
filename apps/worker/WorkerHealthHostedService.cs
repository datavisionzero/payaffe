using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Worker;

/// <summary>
/// Refreshes the heartbeat that the container healthcheck reads, but only
/// while this process can still reach the product database.
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
    ILogger<WorkerHealthHostedService> logger) : BackgroundService
{
    private readonly WorkerHealthOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeat = new WorkerHeartbeatFile(_options.HeartbeatPath);
        using var timer = new PeriodicTimer(_options.ProbeInterval);
        do
        {
            try
            {
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
