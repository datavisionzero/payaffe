using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Telemetry;

/// <summary>
/// Publishes the operational state gauges that cannot be derived from counters,
/// such as how many Reorg Alerts are still open right now.
/// </summary>
/// <remarks>
/// This service deliberately holds no worker lease. Every instance reports the
/// same database-wide values, and the observability stack aggregates them, so a
/// lease would only hide the state when the lease holder is the unhealthy one.
/// </remarks>
public sealed class OperationalMetricsHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OperationalMetricsOptions> options,
    ILogger<OperationalMetricsHostedService> logger,
    SchemaMigrationState schemaMigration) : SchemaGatedBackgroundService(schemaMigration)
{
    private readonly OperationalMetricsOptions _options = options.Value;

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.SnapshotInterval);
        do
        {
            try
            {
                await RecordSnapshotAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A missing metric snapshot must never stop the host that also
                // runs the payment workers.
                logger.LogWarning(exception, "Operational metric snapshot failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RecordSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();

        var leases = await dbContext.BackgroundWorkerLeases
            .AsNoTracking()
            .Select(lease => new { lease.WorkerName, lease.ConsecutiveFailureCount })
            .ToListAsync(cancellationToken);
        foreach (var lease in leases)
        {
            PayaffeTelemetry.RecordWorkerConsecutiveFailures(lease.WorkerName, lease.ConsecutiveFailureCount);
        }

        PayaffeTelemetry.RecordWebhookTerminalFailures(
            await dbContext.WebhookOutboxEvents
                .CountAsync(webhookEvent => webhookEvent.Status == "terminal_failed", cancellationToken));

        var observationHealth = await dbContext.ObservationHealth
            .AsNoTracking()
            .Select(health => new { health.SupportedCurrency, health.Status })
            .ToListAsync(cancellationToken);
        foreach (var health in observationHealth)
        {
            PayaffeTelemetry.RecordObservationUnavailable(
                health.SupportedCurrency,
                string.Equals(health.Status, "unavailable", StringComparison.Ordinal));
        }

        PayaffeTelemetry.RecordAddressPoolAvailable(
            "ETH",
            await dbContext.NativeEthAddresses
                .CountAsync(address => address.Status == "unused", cancellationToken));

        PayaffeTelemetry.RecordOpenReorgAlerts(
            await dbContext.ReorgAlerts
                .CountAsync(alert => alert.Status == "open", cancellationToken));
    }
}
