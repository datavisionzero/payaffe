using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Payaffe.Api.Tests;

/// <summary>
/// The API host runs the background workers unless a deployment moves them to
/// the dedicated worker host, so both sides of that switch are pinned here.
/// </summary>
public sealed class WorkerTopologyTests
{
    [Fact]
    public void Api_host_runs_the_background_workers_by_default()
    {
        using var factory = CreateFactory(runWorkersInApiHost: null);

        Assert.Contains(ProductHostedServices(factory), service => service is PaymentLifecycleHostedService);
        Assert.Contains(ProductHostedServices(factory), service => service is WebhookDeliveryHostedService);
        Assert.Contains(ProductHostedServices(factory), service => service is OperationalMetricsHostedService);
    }

    [Fact]
    public void Api_host_runs_no_background_worker_when_the_worker_host_owns_them()
    {
        using var factory = CreateFactory(runWorkersInApiHost: false);

        Assert.Empty(ProductHostedServices(factory));
    }

    /// <summary>
    /// The scheduled workers, and only those. The framework registers hosted
    /// services of its own, such as data protection, which the process
    /// topology decision does not cover — and so does the startup schema
    /// migration (ADR 0027), which runs in either topology because both hosts
    /// need the schema. Deriving from
    /// <see cref="SchemaGatedBackgroundService"/> is what makes something a
    /// scheduled worker, so that is what this asks.
    /// </summary>
    private static IHostedService[] ProductHostedServices(WebApplicationFactory<Program> factory)
    {
        return [.. factory.Services
            .GetServices<IHostedService>()
            .Where(service => service is SchemaGatedBackgroundService)];
    }

    private static WebApplicationFactory<Program> CreateFactory(bool? runWorkersInApiHost)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                // Registration, not execution, is under test, so every worker
                // stays idle while the host is running. `UseSetting` is the
                // path that reaches configuration before the host registers
                // its services.
                builder.UseSetting("ExchangeRates:RefreshWorker:Enabled", "false");
                builder.UseSetting("Observability:OperationalMetrics:Enabled", "false");
                if (runWorkersInApiHost is not null)
                {
                    builder.UseSetting("Workers:RunInApiHost", runWorkersInApiHost.Value ? "true" : "false");
                }
            });
    }
}
