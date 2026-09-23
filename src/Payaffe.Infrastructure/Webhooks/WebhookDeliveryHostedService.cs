using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Webhooks;

public sealed class WebhookDeliveryHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebhookDeliveryOptions> options,
    ILogger<WebhookDeliveryHostedService> logger,
    SchemaMigrationState schemaMigration) : SchemaGatedBackgroundService(schemaMigration)
{
    private readonly WebhookDeliveryOptions _options = options.Value;
    private readonly int _maxEventsPerPoll = Math.Max(1, options.Value.MaxEventsPerPoll);
    private readonly TimeSpan _pollInterval = options.Value.PollInterval > TimeSpan.Zero
        ? options.Value.PollInterval
        : TimeSpan.FromSeconds(10);

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Webhook Delivery worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            await ProcessBatchAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private const string WorkerName = "webhook-delivery";

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        string? failure = null;
        try
        {
            for (var processedCount = 0; processedCount < _maxEventsPerPoll; processedCount++)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
                var processing = await processor.ProcessNextEventAsync(cancellationToken);
                if (processing == WebhookEventProcessing.None)
                {
                    break;
                }

                // The event itself is already counted as a failed attempt; the
                // batch goes on with the others.
                if (processing == WebhookEventProcessing.Failed)
                {
                    failure = WebhookDeliveryProcessor.ProcessingFailedErrorCode;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Webhook Delivery worker failed while processing a batch.");
            failure = "worker.batch_failed";
        }

        PayaffeTelemetry.RecordWorkerRun(WorkerName, failure is null ? "completed" : "failed");
        await RecordRunAsync(failure, cancellationToken);
    }

    /// <summary>
    /// Webhook Delivery excludes concurrent work per event rather than with a
    /// named worker lease, but it reports every batch to the same row, so the
    /// repeated-failure alert sees a delivery worker that keeps failing.
    /// </summary>
    private async Task RecordRunAsync(string? failure, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var leases = scope.ServiceProvider.GetRequiredService<BackgroundWorkerLeaseManager>();
            await leases.RecordRunAsync(
                WorkerName,
                DateTimeOffset.UtcNow,
                succeeded: failure is null,
                failure,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Webhook Delivery worker could not record the outcome of a batch.");
        }
    }
}
