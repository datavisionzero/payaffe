using Payaffe.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Integration.Tests.Webhooks;

public sealed class ConfigurationWebhookSecretResolverTests
{
    [Fact]
    public async Task ResolveAsync_returns_configured_endpoint_secret()
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            ["Webhooks:EndpointSecrets:checkout"] = "configured-secret",
        });

        var secret = await resolver.ResolveAsync(
            "configuration:Webhooks:EndpointSecrets:checkout",
            CancellationToken.None);

        Assert.Equal("configured-secret", secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("secret://webhooks/checkout")]
    [InlineData("configuration:")]
    [InlineData("configuration:ConnectionStrings:Payaffe")]
    [InlineData("configuration:Webhooks:EndpointSecrets:")]
    [InlineData("configuration:Webhooks:EndpointSecrets:missing")]
    public async Task ResolveAsync_returns_null_for_unsupported_or_missing_references(string secretReference)
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Payaffe"] = "Host=db;Password=database-secret",
            ["Webhooks:EndpointSecrets:checkout"] = "configured-secret",
        });

        var secret = await resolver.ResolveAsync(secretReference, CancellationToken.None);

        Assert.Null(secret);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_blank_configured_secret()
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            ["Webhooks:EndpointSecrets:checkout"] = "   ",
        });

        var secret = await resolver.ResolveAsync(
            "configuration:Webhooks:EndpointSecrets:checkout",
            CancellationToken.None);

        Assert.Null(secret);
    }

    private static ConfigurationWebhookSecretResolver CreateResolver(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new ConfigurationWebhookSecretResolver(configuration);
    }
}
