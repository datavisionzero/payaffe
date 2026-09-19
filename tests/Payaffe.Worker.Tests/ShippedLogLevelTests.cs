using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Payaffe.Worker.Tests;

/// <summary>
/// What the worker host logs before an installation configures anything. The
/// host reads its own `appsettings.json` here, so this measures the file that
/// ships rather than a rebuilt copy of it.
///
/// The first installation to run in production sent 93 percent of its log
/// entries from categories nobody reads when things work, and an operator
/// either stores that or turns it off per installation. What the product is
/// chatty about by default is a decision, and this is where it is written down.
/// </summary>
public sealed class ShippedLogLevelTests
{
    /// <summary>
    /// `HttpClient` writes four Information lines per call -- two loggers,
    /// start and end -- carrying a status code and a duration. The observation
    /// worker polls once per active Payment Address, so the volume follows the
    /// installation's business rather than its problems.
    /// </summary>
    [Theory]
    [InlineData("System.Net.Http.HttpClient.CoinGeckoExchangeRateSource.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.CoinGeckoExchangeRateSource.ClientHandler")]
    [InlineData("System.Net.Http.HttpClient.NownodesBlockchainObservationAdapter.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.NownodesBlockchainObservationAdapter.ClientHandler")]
    public void A_provider_call_that_worked_is_not_an_entry(string category)
    {
        var logger = CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    /// <summary>
    /// The other half of the same decision: quieting the framework must not
    /// quiet the product. Everything payaffe says about its own work is still
    /// delivered.
    /// </summary>
    [Theory]
    [InlineData("Payaffe.Infrastructure.Payments.RateCacheRefreshHostedService")]
    [InlineData("Payaffe.Infrastructure.Payments.BlockchainObservationHostedService")]
    [InlineData("Payaffe.Infrastructure.Payments.PaymentLifecycleHostedService")]
    [InlineData("Payaffe.Infrastructure.Webhooks.WebhookDeliveryHostedService")]
    public void The_product_still_speaks_at_information(string category)
    {
        Assert.True(CreateLogger(category).IsEnabled(LogLevel.Information));
    }

    private static ILogger CreateLogger(string category)
    {
        var host = Host.CreateApplicationBuilder().Build();
        return host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
    }
}
