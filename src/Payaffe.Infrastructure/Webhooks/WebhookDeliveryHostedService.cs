using Payaffe.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Webhooks;

public sealed class WebhookDeliveryHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebhookDeliveryOptions> options,
    ILogger<WebhookDeliveryHostedService> logger) : BackgroundService
{
    private readonly WebhookDeliveryOptions _options = options.Value;
    private readonly int _maxEventsPerPoll = Math.Max(1, options.Value.MaxEventsPerPoll);
    private readonly TimeSpan _pollInterval = options.Value.PollInterval > TimeSpan.Zero
        ? options.Value.PollInterval
        : TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
        try
        {
            for (var processedCount = 0; processedCount < _maxEventsPerPoll; processedCount++)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
                var processed = await processor.ProcessNextAsync(cancellationToken);
                if (!processed)
                {
                    break;
                }
            }

            PayaffeTelemetry.RecordWorkerRun(WorkerName, "completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Webhook Delivery worker failed while processing a batch.");
            PayaffeTelemetry.RecordWorkerRun(WorkerName, "failed");
        }
    }
}
