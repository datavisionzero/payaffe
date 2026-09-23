using Payaffe.Application;
using Payaffe.Application.Installation;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Payaffe.Api.Tests.Payments;

public sealed class SimulatedExchangeRateSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-23T12:00:00Z");

    [Theory]
    [InlineData("EUR", 1999, "BTC", "50000", "0.0003998")]
    [InlineData("USD", 1000, "LTC", "90", "0.11111112")]
    [InlineData("EUR", 2500, "ETH", "2500", "0.01")]
    public async Task The_same_fiat_amount_always_locks_the_same_crypto_amount(
        string fiatCurrency,
        long fiatAmountMinor,
        string supportedCurrency,
        string expectedRate,
        string expectedAmount)
    {
        var source = new SimulatedExchangeRateSource(Options.Create(new SimulatedExchangeRateOptions()));

        var first = await source.GetRateLockQuoteAsync(fiatCurrency, fiatAmountMinor, supportedCurrency, Now, CancellationToken.None);
        var second = await source.GetRateLockQuoteAsync(fiatCurrency, fiatAmountMinor, supportedCurrency, Now.AddDays(3), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(SimulatedExchangeRateSource.SourceName, first.RateSource);
        Assert.Equal(expectedRate, first.RateValue);
        Assert.Equal(expectedAmount, first.ExpectedCryptoAmount);
        Assert.Equal(first.ExpectedCryptoAmount, second!.ExpectedCryptoAmount);
        Assert.True(await source.IsRateAvailableAsync(fiatCurrency, supportedCurrency, Now, CancellationToken.None));
    }

    [Fact]
    public async Task Configured_rates_replace_the_defaults()
    {
        var source = new SimulatedExchangeRateSource(Options.Create(new SimulatedExchangeRateOptions { BtcEur = 10_000m }));

        var quote = await source.GetRateLockQuoteAsync("EUR", 10_000, "BTC", Now, CancellationToken.None);

        Assert.Equal("0.01", quote!.ExpectedCryptoAmount);
    }

    [Fact]
    public async Task Unsupported_pairs_have_no_rate()
    {
        var source = new SimulatedExchangeRateSource(Options.Create(new SimulatedExchangeRateOptions()));

        Assert.Null(await source.GetRateLockQuoteAsync("GBP", 1000, "BTC", Now, CancellationToken.None));
        Assert.False(await source.IsRateAvailableAsync("EUR", "XMR", Now, CancellationToken.None));
    }

    [Fact]
    public void A_rate_of_zero_is_a_configuration_error()
    {
        var result = new SimulatedExchangeRateOptionsValidator().Validate(null, new SimulatedExchangeRateOptions { EthUsd = 0 });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("test", typeof(SimulatedExchangeRateSource))]
    [InlineData("live", typeof(CoinGeckoExchangeRateSource))]
    public void The_installation_mode_decides_the_exchange_rate_source(string mode, Type expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(ConfiguredInstallationMode.FromConfiguration(mode));
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure("Host=unused");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IExchangeRateSource>());
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IRateCacheRefresher>());
        if (mode == "test")
        {
            // Nothing in Test Mode can reach CoinGecko: the client is not even registered.
            Assert.Null(scope.ServiceProvider.GetService<CoinGeckoExchangeRateSource>());
        }
    }
}
