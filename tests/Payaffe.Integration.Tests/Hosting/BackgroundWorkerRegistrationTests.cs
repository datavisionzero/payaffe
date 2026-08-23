using Payaffe.Application;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Payaffe.Integration.Tests.Hosting;

/// <summary>
/// The process topology depends on exactly which hosted services the shared
/// infrastructure registration adds, so the inventory is pinned here. Adding a
/// worker has to be a deliberate change to this list.
/// </summary>
public sealed class BackgroundWorkerRegistrationTests
{
    private const string UnusedConnectionString = "Host=localhost;Database=payaffe";

    [Fact]
    public void Infrastructure_registers_the_full_background_worker_inventory_by_default()
    {
        var hostedServiceTypes = BuildServices(registerHostedWorkers: true)
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        Assert.Equal(
            [
                typeof(RateCacheRefreshHostedService),
                typeof(PaymentLifecycleHostedService),
                typeof(BlockchainObservationHostedService),
                typeof(ReorgMonitoringHostedService),
                typeof(WebhookDeliveryHostedService),
                typeof(OperationalMetricsHostedService),
            ],
            hostedServiceTypes);
    }

    /// <summary>
    /// A deployment that runs the dedicated worker host, or a request-only host
    /// such as the Admin MCP, must not start a second copy of any worker.
    /// </summary>
    [Fact]
    public void Infrastructure_registers_no_background_worker_for_a_request_only_host()
    {
        var hostedServices = BuildServices(registerHostedWorkers: false)
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ToArray();

        Assert.Empty(hostedServices);
    }

    private static IServiceCollection BuildServices(bool registerHostedWorkers)
    {
        var services = new ServiceCollection();
        // A real host supplies this; a bare ServiceCollection does not.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(UnusedConnectionString, registerHostedWorkers);
        return services;
    }
}
