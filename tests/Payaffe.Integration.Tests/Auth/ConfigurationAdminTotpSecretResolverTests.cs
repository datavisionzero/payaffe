using Payaffe.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Integration.Tests.Auth;

public sealed class ConfigurationAdminTotpSecretResolverTests
{
    [Fact]
    public async Task Resolves_base32_secret_from_restricted_admin_subtree()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:TotpSecrets:first-admin"] = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
            })
            .Build();
        var resolver = new ConfigurationAdminTotpSecretResolver(configuration);

        var result = await resolver.ResolveSecretAsync(
            "configuration:Admin:TotpSecrets:first-admin",
            CancellationToken.None);

        Assert.Equal("12345678901234567890"u8.ToArray(), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("environment:Admin:TotpSecrets:first-admin")]
    [InlineData("configuration:ConnectionStrings:Payaffe")]
    [InlineData("configuration:Admin:TotpSecrets:")]
    [InlineData("configuration:Admin:TotpSecrets:missing")]
    [InlineData("configuration:Admin:TotpSecrets:invalid")]
    [InlineData("configuration:Admin:TotpSecrets:short")]
    public async Task Rejects_missing_invalid_or_out_of_scope_references(string secretReference)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:TotpSecrets:invalid"] = "not-base32!",
                ["Admin:TotpSecrets:short"] = "MZXW6===",
            })
            .Build();
        var resolver = new ConfigurationAdminTotpSecretResolver(configuration);

        var result = await resolver.ResolveSecretAsync(secretReference, CancellationToken.None);

        Assert.Null(result);
    }
}
