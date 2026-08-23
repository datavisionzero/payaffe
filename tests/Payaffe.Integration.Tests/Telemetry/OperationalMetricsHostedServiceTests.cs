using System.Diagnostics.Metrics;
using Payaffe.Application;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Telemetry;

/// <summary>
/// The operational gauges are read straight from the product tables, so they
/// are verified against a migrated PostgreSQL schema rather than a fake.
/// </summary>
public sealed class OperationalMetricsHostedServiceTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T10:00:00Z");
    private static readonly Guid AdminAccountId = Guid.Parse("2b0f3a1c-8b0e-4c31-9f77-2f2b9a0f4c11");

    [Fact]
    public async Task Snapshot_publishes_the_operational_state_of_a_migrated_installation()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var serviceProvider = await BuildServiceProviderAsync(connectionString);
        await SeedAsync(serviceProvider);

        var measurements = await CaptureSnapshotAsync(serviceProvider);

        Assert.Equal(3, measurements["payaffe.worker.consecutive_failures|worker.name=blockchain-observation"]);
        Assert.Equal(0, measurements["payaffe.worker.consecutive_failures|worker.name=payment-lifecycle"]);
        Assert.Equal(1, measurements["payaffe.observation.unavailable|currency=ETH"]);
        Assert.Equal(0, measurements["payaffe.observation.unavailable|currency=BTC"]);
        Assert.Equal(2, measurements["payaffe.address_pool.available|currency=ETH"]);
        Assert.Equal(0, measurements["payaffe.reorg_alerts.open|"]);
        Assert.Equal(0, measurements["payaffe.webhook.terminal_failures|"]);
    }

    /// <summary>
    /// The product meter is process-global, so a disabled snapshot is verified
    /// by the service finishing instead of by an absence of measurements.
    /// </summary>
    [Fact]
    public async Task Disabled_snapshot_stops_without_running()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var serviceProvider = await BuildServiceProviderAsync(connectionString, enabled: false);

        var hostedService = SnapshotServiceOf(serviceProvider);
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            Assert.NotNull(hostedService.ExecuteTask);
            await hostedService.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(hostedService.ExecuteTask.IsCompletedSuccessfully);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Enabled_snapshot_keeps_running_between_intervals()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var serviceProvider = await BuildServiceProviderAsync(connectionString);

        var hostedService = SnapshotServiceOf(serviceProvider);
        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            Assert.NotNull(hostedService.ExecuteTask);
            Assert.False(hostedService.ExecuteTask.IsCompleted);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<Dictionary<string, long>> CaptureSnapshotAsync(IServiceProvider serviceProvider)
    {
        var measurements = new Dictionary<string, long>(StringComparer.Ordinal);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (string.Equals(instrument.Meter.Name, PayaffeTelemetry.MeterName, StringComparison.Ordinal))
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagText = string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
            lock (measurements)
            {
                measurements[$"{instrument.Name}|{tagText}"] = measurement;
            }

            // The open Reorg Alert gauge is the last value in one snapshot.
            if (string.Equals(instrument.Name, "payaffe.reorg_alerts.open", StringComparison.Ordinal))
            {
                captured.TrySetResult();
            }
        });
        listener.Start();

        var hostedService = SnapshotServiceOf(serviceProvider);
        await hostedService.StartAsync(CancellationToken.None);
        await captured.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await hostedService.StopAsync(CancellationToken.None);
        lock (measurements)
        {
            return new Dictionary<string, long>(measurements, StringComparer.Ordinal);
        }
    }

    private static OperationalMetricsHostedService SnapshotServiceOf(IServiceProvider serviceProvider) =>
        serviceProvider.GetServices<IHostedService>().OfType<OperationalMetricsHostedService>().Single();

    private static async Task<ServiceProvider> BuildServiceProviderAsync(string connectionString, bool enabled = true)
    {
        var services = new ServiceCollection();
        // A real host supplies this; a bare ServiceCollection does not.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.Configure<OperationalMetricsOptions>(options =>
        {
            options.Enabled = enabled;
            options.SnapshotInterval = TimeSpan.FromSeconds(5);
        });

        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);
        return serviceProvider;
    }

    private static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();

        dbContext.BackgroundWorkerLeases.Add(CreateLease("blockchain-observation", consecutiveFailureCount: 3));
        dbContext.BackgroundWorkerLeases.Add(CreateLease("payment-lifecycle", consecutiveFailureCount: 0));
        dbContext.ObservationHealth.Add(CreateObservationHealth("ETH", "unavailable"));
        dbContext.ObservationHealth.Add(CreateObservationHealth("BTC", "available"));

        dbContext.AdminAccounts.Add(new AdminAccountRecord
        {
            Id = AdminAccountId,
            Username = "pool-operator",
            NormalizedUsername = "pool-operator",
            PasswordHash = "not-used-in-this-test",
            Status = "active",
            CreatedAt = Now,
            UpdatedAt = Now,
        });

        var importId = Guid.NewGuid();
        dbContext.NativeEthAddressPoolImports.Add(new NativeEthAddressPoolImportRecord
        {
            Id = importId,
            ImportedByAdminAccountId = AdminAccountId,
            AddressCount = 3,
            ImportedAt = Now,
        });
        dbContext.NativeEthAddresses.Add(CreateAddress(importId, "0x0000000000000000000000000000000000000001", "unused"));
        dbContext.NativeEthAddresses.Add(CreateAddress(importId, "0x0000000000000000000000000000000000000002", "unused"));
        dbContext.NativeEthAddresses.Add(CreateAddress(importId, "0x0000000000000000000000000000000000000003", "retired"));

        await dbContext.SaveChangesAsync();
    }

    private static BackgroundWorkerLeaseRecord CreateLease(string workerName, int consecutiveFailureCount) => new()
    {
        WorkerName = workerName,
        ConsecutiveFailureCount = consecutiveFailureCount,
        UpdatedAt = Now,
        Version = 1,
    };

    private static ObservationHealthRecord CreateObservationHealth(string supportedCurrency, string status) => new()
    {
        SupportedCurrency = supportedCurrency,
        ProviderName = "test-provider",
        Status = status,
        UpdatedAt = Now,
        Version = 1,
    };

    private static NativeEthAddressRecord CreateAddress(Guid importId, string address, string status) => new()
    {
        Id = Guid.NewGuid(),
        ImportId = importId,
        Address = address,
        Status = status,
        CreatedAt = Now,
        UpdatedAt = Now,
        Version = 1,
    };
}
