using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class PaymentLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentLifecycleWorkerOptions> options,
    ILogger<PaymentLifecycleHostedService> logger) : BackgroundService
{
    private readonly PaymentLifecycleWorkerOptions _options = options.Value;
    private readonly int _expirationBatchSize = Math.Max(1, options.Value.ExpirationBatchSize);
    private readonly TimeSpan _pollInterval = options.Value.PollInterval > TimeSpan.Zero
        ? options.Value.PollInterval
        : TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Payment lifecycle worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            await ProcessOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private const string WorkerName = "payment-lifecycle";

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
            var result = await payments.ExpireDuePaymentsAsync(_expirationBatchSize, cancellationToken);
            if (result.ExpiredCount > 0)
            {
                logger.LogInformation("Expired {ExpiredCount} due Payments.", result.ExpiredCount);
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
            logger.LogError(exception, "Payment lifecycle worker failed while processing due work.");
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
                logger.LogError(releaseException, "Payment lifecycle worker could not release its lease after a failure.");
            }
        }
    }
}
