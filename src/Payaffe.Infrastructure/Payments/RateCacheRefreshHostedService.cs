using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class RateCacheRefreshHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<RateCacheRefreshWorkerOptions> options,
    ILogger<RateCacheRefreshHostedService> logger,
    SchemaMigrationState schemaMigration)
    : SchemaGatedBackgroundService(schemaMigration)
{
    private const string WorkerName = "rate-cache-refresh";

    private readonly RateCacheRefreshWorkerOptions _options = options.Value;

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Rate Cache refresh worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshOnceAsync(stoppingToken);
            await Task.Delay(_options.RefreshInterval, stoppingToken);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var lease = scope.ServiceProvider.GetRequiredService<BackgroundWorkerLeaseManager>();
        var acquired = false;
        try
        {
            acquired = await lease.TryAcquireAsync(
                WorkerName,
                DateTimeOffset.UtcNow,
                _options.LeaseDuration,
                cancellationToken);
            if (!acquired)
            {
                PayaffeTelemetry.RecordWorkerRun(WorkerName, "skipped");
                return;
            }

            var refresher = scope.ServiceProvider.GetRequiredService<IRateCacheRefresher>();
            var result = await refresher.RefreshAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (result.RefreshedPairCount == result.RequestedPairCount)
            {
                logger.LogInformation(
                    "Rate Cache refresh completed for {PairCount} currency pairs.",
                    result.RefreshedPairCount);
            }
            else
            {
                logger.LogWarning(
                    "Rate Cache refresh updated {RefreshedPairCount} of {RequestedPairCount} currency pairs.",
                    result.RefreshedPairCount,
                    result.RequestedPairCount);
            }

            await lease.CompleteAsync(WorkerName, DateTimeOffset.UtcNow, cancellationToken);
            PayaffeTelemetry.RecordWorkerRun(WorkerName, "completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Rate Cache refresh failed.");
            PayaffeTelemetry.RecordWorkerRun(WorkerName, "failed");
            if (!acquired)
            {
                return;
            }

            try
            {
                await lease.FailAsync(
                    WorkerName,
                    DateTimeOffset.UtcNow,
                    "worker.batch_failed",
                    cancellationToken);
            }
            catch (Exception releaseException)
            {
                logger.LogError(releaseException, "Rate Cache refresh worker could not release its lease after a failure.");
            }
        }
    }
}
