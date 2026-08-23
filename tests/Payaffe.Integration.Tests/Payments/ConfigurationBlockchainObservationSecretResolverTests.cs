using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Integration.Tests.Payments;

public sealed class ConfigurationBlockchainObservationSecretResolverTests
{
    [Fact]
    public async Task ResolveAsync_returns_configured_provider_secret()
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            ["BlockchainObservation:ProviderSecrets:blockchair"] = "configured-api-key",
        });

        var secret = await resolver.ResolveAsync(
            "configuration:BlockchainObservation:ProviderSecrets:blockchair",
            CancellationToken.None);

        Assert.Equal("configured-api-key", secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("secret://blockchain-observation/blockchair")]
    [InlineData("configuration:")]
    [InlineData("configuration:ConnectionStrings:Payaffe")]
    [InlineData("configuration:Webhooks:EndpointSecrets:checkout")]
    [InlineData("configuration:BlockchainObservation:ProviderSecrets:")]
    [InlineData("configuration:BlockchainObservation:ProviderSecrets:missing")]
    public async Task ResolveAsync_returns_null_for_unsupported_or_missing_references(string secretReference)
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Payaffe"] = "Host=db;Password=database-secret",
            ["Webhooks:EndpointSecrets:checkout"] = "webhook-secret",
            ["BlockchainObservation:ProviderSecrets:blockchair"] = "configured-api-key",
        });

        var secret = await resolver.ResolveAsync(secretReference, CancellationToken.None);

        Assert.Null(secret);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_blank_configured_secret()
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            ["BlockchainObservation:ProviderSecrets:blockchair"] = "   ",
        });

        var secret = await resolver.ResolveAsync(
            "configuration:BlockchainObservation:ProviderSecrets:blockchair",
            CancellationToken.None);

        Assert.Null(secret);
    }

    private static ConfigurationBlockchainObservationSecretResolver CreateResolver(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new ConfigurationBlockchainObservationSecretResolver(configuration);
    }
}
