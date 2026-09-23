using System.Net;
using Payaffe.Application.Webhooks;

namespace Payaffe.Application.Tests.Webhooks;

public sealed class WebhookTargetPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void Non_public_addresses_are_refused_by_default(string address)
    {
        Assert.False(WebhookTargetPolicy.IsPublic(IPAddress.Parse(address)));
        Assert.False(WebhookTargetPolicy.PublicOnly.AllowsAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::6810:84e5")]
    [InlineData("::ffff:93.184.215.14")]
    public void Public_addresses_are_allowed(string address)
    {
        Assert.True(WebhookTargetPolicy.PublicOnly.AllowsAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void An_allowlist_names_hosts_addresses_and_networks()
    {
        var policy = WebhookTargetPolicy.Parse("shop.internal, 10.20.0.0/16; 192.168.1.5 fd00::/8");

        Assert.True(policy.AllowsHost("SHOP.internal."));
        Assert.False(policy.AllowsHost("other.internal"));
        Assert.True(policy.AllowsAddress(IPAddress.Parse("10.20.3.4")));
        Assert.False(policy.AllowsAddress(IPAddress.Parse("10.21.3.4")));
        Assert.True(policy.AllowsAddress(IPAddress.Parse("192.168.1.5")));
        Assert.True(policy.AllowsAddress(IPAddress.Parse("::ffff:192.168.1.5")));
        Assert.False(policy.AllowsAddress(IPAddress.Parse("192.168.1.6")));
        Assert.True(policy.AllowsAddress(IPAddress.Parse("fd00::1")));
    }

    [Theory]
    [InlineData("not a host!")]
    [InlineData("10.0.0.0/33")]
    [InlineData("http://shop.internal")]
    public void An_entry_that_is_none_of_them_is_refused(string configured)
    {
        var errors = WebhookTargetPolicy.Validate(configured);

        var error = Assert.Single(errors);
        Assert.Contains(WebhookTargetPolicy.ConfigurationKey, error, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => WebhookTargetPolicy.Parse(configured));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest", true)]
    [InlineData("https://[::1]/hooks", true)]
    [InlineData("https://10.0.0.8/hooks", true)]
    [InlineData("https://93.184.215.14/hooks", false)]
    [InlineData("https://shop.example.test/hooks", false)]
    [InlineData("https://localhost/hooks", false)]
    public void A_literal_non_public_target_is_refused_when_an_endpoint_is_saved(string url, bool refused)
    {
        Assert.Equal(refused, WebhookTargetPolicy.PublicOnly.RefusesLiteralTarget(new Uri(url)));
    }

    [Fact]
    public void An_allowlisted_literal_target_is_accepted_when_an_endpoint_is_saved()
    {
        var policy = WebhookTargetPolicy.Parse("10.0.0.0/24,::1");

        Assert.False(policy.RefusesLiteralTarget(new Uri("https://10.0.0.8/hooks")));
        Assert.False(policy.RefusesLiteralTarget(new Uri("https://[::1]/hooks")));
        Assert.True(policy.RefusesLiteralTarget(new Uri("https://10.0.1.8/hooks")));
    }
}
