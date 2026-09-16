using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class BlockchainObservationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<BlockchainObservationWorkerOptions> options,
    ILogger<BlockchainObservationHostedService> logger,
    SchemaMigrationState schemaMigration) : SchemaGatedBackgroundService(schemaMigration)
{
    private readonly BlockchainObservationWorkerOptions _options = options.Value;
    private readonly int _maxPaymentsPerPoll = Math.Max(1, options.Value.MaxPaymentsPerPoll);
    private readonly TimeSpan _pollInterval = options.Value.PollInterval > TimeSpan.Zero
        ? options.Value.PollInterval
        : TimeSpan.FromMinutes(1);

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Blockchain Observation worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            await ProcessOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private const string WorkerName = "blockchain-observation";

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
            var result = await payments.PollBlockchainObservationsAsync(_maxPaymentsPerPoll, cancellationToken);
            if (result.ObservationCount > 0 || result.FailedCount > 0)
            {
                logger.LogInformation(
                    "Polled {TargetCount} Blockchain Observation targets, processed {ObservationCount} observations and isolated {FailedCount} failures.",
                    result.TargetCount,
                    result.ObservationCount,
                    result.FailedCount);
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
            logger.LogError(exception, "Blockchain Observation worker failed while processing due work.");
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
                logger.LogError(releaseException, "Blockchain Observation worker could not release its lease after a failure.");
            }
        }
    }
}
