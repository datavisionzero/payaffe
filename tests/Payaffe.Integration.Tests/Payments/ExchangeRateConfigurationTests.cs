using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Integration.Tests.Payments;

public sealed class ExchangeRateConfigurationTests
{
    [Fact]
    public async Task Resolves_api_key_only_from_exchange_rate_provider_subtree()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExchangeRates:ProviderSecrets:coingecko"] = "configured-key",
                ["ConnectionStrings:Payaffe"] = "not-allowed",
            })
            .Build();
        var resolver = new ConfigurationExchangeRateSecretResolver(configuration);

        var resolved = await resolver.ResolveAsync(
            "configuration:ExchangeRates:ProviderSecrets:coingecko",
            CancellationToken.None);
        var rejected = await resolver.ResolveAsync(
            "configuration:ConnectionStrings:Payaffe",
            CancellationToken.None);

        Assert.Equal("configured-key", resolved);
        Assert.Null(rejected);
    }

    [Fact]
    public void Validator_rejects_invalid_cache_and_secret_configuration()
    {
        var result = new ExchangeRateOptionsValidator().Validate(
            null,
            new ExchangeRateOptions
            {
                CacheInterval = TimeSpan.FromMinutes(10),
                MaxStaleAge = TimeSpan.FromMinutes(5),
                ApiKeyReference = "configuration:ConnectionStrings:Payaffe",
                RequestTimeout = TimeSpan.Zero,
            });

        Assert.False(result.Succeeded);
        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
        Assert.Contains(failures, failure => failure.Contains("MaxStaleAge", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("ApiKeyReference", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("RequestTimeout", StringComparison.Ordinal));
    }
}
