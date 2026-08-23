using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class ReorgMonitoringHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReorgMonitoringWorkerOptions> options,
    ILogger<ReorgMonitoringHostedService> logger,
    SchemaMigrationState schemaMigration) : SchemaGatedBackgroundService(schemaMigration)
{
    private readonly ReorgMonitoringWorkerOptions _options = options.Value;
    private readonly int _maxTransactionsPerPoll = Math.Max(1, options.Value.MaxTransactionsPerPoll);
    private readonly TimeSpan _pollInterval = options.Value.PollInterval > TimeSpan.Zero
        ? options.Value.PollInterval
        : TimeSpan.FromMinutes(5);

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Reorg Monitoring worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            await ProcessOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private const string WorkerName = "reorg-monitoring";

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
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

            var payments = scope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
            var result = await payments.MonitorBlockchainReorgsAsync(_maxTransactionsPerPoll, cancellationToken);
            if (result.CheckedCount > 0 || result.ReorgAlertCount > 0)
            {
                logger.LogInformation(
                    "Checked {CheckedCount} Reorg Monitoring targets and created {ReorgAlertCount} Reorg Alerts.",
                    result.CheckedCount,
                    result.ReorgAlertCount);
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
            logger.LogError(exception, "Reorg Monitoring worker failed while processing due work.");
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
                logger.LogError(releaseException, "Reorg Monitoring worker could not release its lease after a failure.");
            }
        }
    }
}
