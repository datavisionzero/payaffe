using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Payaffe.Api.Tests.Payments;

namespace Payaffe.Api.Tests;

/// <summary>
/// The API host's half of the same decision the Worker host pins: what an
/// installation is told about before it configures anything. The API calls the
/// rate provider on its own while a payer selects a currency, so it is chatty
/// in the same way and for the same reason.
/// </summary>
public sealed class ShippedLogLevelTests
{
    [Theory]
    [InlineData("System.Net.Http.HttpClient.CoinGeckoExchangeRateSource.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.CoinGeckoExchangeRateSource.ClientHandler")]
    public async Task A_provider_call_that_worked_is_not_an_entry(string category)
    {
        await using var factory = new PaymentApiFactory();
        _ = factory.CreateClient();

        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public async Task The_product_still_speaks_at_information()
    {
        await using var factory = new PaymentApiFactory();
        _ = factory.CreateClient();

        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Payaffe.Api");

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }
}
