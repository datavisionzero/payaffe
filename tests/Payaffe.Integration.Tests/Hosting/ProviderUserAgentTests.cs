using Payaffe.Application;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Hosting;

/// <summary>
/// CoinGecko answers requests without a `User-Agent` with HTTP 403, and
/// `HttpClient` sends none by default. A missing header would leave the Rate
/// Cache permanently empty and make Currency Selection fail on every Payment,
/// so the header is pinned for every outbound client.
/// </summary>
public sealed class ProviderUserAgentTests
{
    [Theory]
    [InlineData(nameof(CoinGeckoExchangeRateSource))]
    [InlineData(nameof(BlockchairBlockchainObservationAdapter))]
    [InlineData(nameof(NownodesBlockchainObservationAdapter))]
    [InlineData(nameof(WebhookDeliveryProcessor))]
    public void Outbound_clients_identify_the_product(string clientName)
    {
        var services = new ServiceCollection();
        // A real host supplies this; a bare ServiceCollection does not.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure("Host=localhost;Database=payaffe");

        using var serviceProvider = services.BuildServiceProvider();
        using var client = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(clientName);

        Assert.Equal(ProductUserAgent.Value, client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void Product_user_agent_carries_a_product_name_and_version()
    {
        Assert.Equal($"payaffe/{ProductVersion.Value}", ProductUserAgent.Value);
        Assert.DoesNotContain(' ', ProductUserAgent.Value);
    }

    /// <summary>
    /// Source-link stamps the commit into the informational version. It must
    /// not reach an outbound header or a telemetry resource attribute.
    /// </summary>
    [Fact]
    public void Product_version_drops_the_build_metadata()
    {
        Assert.DoesNotContain('+', ProductVersion.Value);
        Assert.Matches(@"^\d+\.\d+\.\d+", ProductVersion.Value);
    }
}
