using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Api.Tests.Http;

/// <summary>
/// Which address this host believes a request came from. Every rate limit
/// partitioned by source address and every audit entry that records one rests
/// on it, so the two failures that matter are opposite: counting the whole
/// internet as one caller behind a proxy, and letting a caller pick its own
/// address by writing a header (ADR 0032).
///
/// The browser error endpoint is the probe, because its limit is per source
/// address and nothing else, and it needs no database and no credential.
/// </summary>
public sealed class TrustedProxyTests
{
    private static readonly IPAddress Proxy = IPAddress.Parse("172.30.0.7");

    /// <summary>
    /// The case this exists for: one proxy in front, and two payers behind it.
    /// </summary>
    [Fact]
    public async Task Behind_a_trusted_proxy_two_forwarded_callers_are_two_partitions()
    {
        await using var factory = CreateFactory(trustedProxies: "172.30.0.0/24");
        using var client = factory.CreateClient();

        var first = await ReportAsync(client, forwardedFor: "198.51.100.10");
        var second = await ReportAsync(client, forwardedFor: "198.51.100.11");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_one_forwarded_caller_is_still_limited()
    {
        await using var factory = CreateFactory(trustedProxies: "172.30.0.0/24");
        using var client = factory.CreateClient();

        await ReportAsync(client, forwardedFor: "198.51.100.10");
        var second = await ReportAsync(client, forwardedFor: "198.51.100.10");

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    /// <summary>
    /// A single address is as valid a list as a network, and the chain is
    /// walked past every named hop rather than cut after one: the address that
    /// remains is the one the outermost proxy saw.
    /// </summary>
    [Fact]
    public async Task A_named_proxy_address_and_a_chain_resolve_to_the_outermost_caller()
    {
        await using var factory = CreateFactory(trustedProxies: "172.30.0.7, 10.1.0.0/16");
        using var client = factory.CreateClient();

        var first = await ReportAsync(client, forwardedFor: "198.51.100.10, 10.1.2.3");
        var second = await ReportAsync(client, forwardedFor: "198.51.100.10, 10.1.9.9");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    /// <summary>
    /// The header is only evidence when the hop that sent it is one of ours. A
    /// caller reaching this host directly cannot spend somebody else's budget
    /// or hide from its own by writing a header.
    /// </summary>
    [Fact]
    public async Task An_untrusted_connection_cannot_name_its_own_address()
    {
        await using var factory = CreateFactory(
            trustedProxies: "172.30.0.0/24",
            connection: IPAddress.Parse("203.0.113.9"));
        using var client = factory.CreateClient();

        await ReportAsync(client, forwardedFor: "198.51.100.10");
        var second = await ReportAsync(client, forwardedFor: "198.51.100.11");

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    /// <summary>
    /// Nothing configured means nothing trusted, which is right for a host
    /// reached directly and is why the setting has to be set.
    /// </summary>
    [Fact]
    public async Task Without_a_configured_proxy_the_header_is_ignored()
    {
        await using var factory = CreateFactory(trustedProxies: null);
        using var client = factory.CreateClient();

        await ReportAsync(client, forwardedFor: "198.51.100.10");
        var second = await ReportAsync(client, forwardedFor: "198.51.100.11");

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unset_list_trusts_nothing(string? configured)
    {
        Assert.True(TrustedProxies.Parse(configured).IsEmpty);
    }

    [Fact]
    public void Addresses_and_networks_are_read_from_one_list()
    {
        var parsed = TrustedProxies.Parse("172.30.0.0/24 10.0.0.4;  ::1\n2001:db8::/32");

        Assert.Equal(4, parsed.Count);
        Assert.Equal(
            ["10.0.0.4", "::1"],
            parsed.Addresses.Select(address => address.ToString()));
        Assert.Equal(
            ["172.30.0.0/24", "2001:db8::/32"],
            parsed.Networks.Select(network => network.ToString()));
    }

    /// <summary>
    /// A list that was meant to be there and silently is not would leave every
    /// limit counting the internet as one caller, so a bad entry stops the
    /// host instead.
    /// </summary>
    [Theory]
    [InlineData("172.30.0.0/24, not-an-address")]
    [InlineData("172.30.0.0/33")]
    [InlineData("172.30.0.0/")]
    public void A_rejected_entry_names_itself_and_stops_the_start(string configured)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => TrustedProxies.Parse(configured));

        Assert.Contains(TrustedProxies.ConfigurationKey, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A prefix written with host bits still names a range, and is read as the
    /// range rather than refused.
    /// </summary>
    [Fact]
    public void A_prefix_with_host_bits_is_read_as_its_network()
    {
        var network = Assert.Single(TrustedProxies.Parse("172.30.0.5/24").Networks);

        Assert.Equal("172.30.0.0/24", network.ToString());
    }

    private static async Task<HttpResponseMessage> ReportAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/client-errors")
        {
            Content = JsonContent.Create(new { name = "TypeError", message = "failed", path = "/pay/abc" }),
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);

        return await client.SendAsync(request);
    }

    private static TrustedProxyFactory CreateFactory(string? trustedProxies, IPAddress? connection = null)
    {
        return new TrustedProxyFactory(trustedProxies, connection ?? Proxy);
    }

    private sealed class TrustedProxyFactory(string? trustedProxies, IPAddress connection)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(TrustedProxies.ConfigurationKey, trustedProxies ?? string.Empty);
            // One report per address, so the second one from the same address
            // is the observable difference between the partitions.
            builder.UseSetting("Diagnostics:ClientErrors:RateLimitPermitLimit", "1");
            builder.ConfigureServices(services =>
            {
                services.RemoveStartupSchemaMigration();
                services.AddSingleton<IStartupFilter>(new ConnectedFrom(connection));
            });
        }
    }

    /// <summary>
    /// The test server has no socket, so the hop the request arrives from is
    /// set here -- ahead of everything the host itself registers, which is
    /// where the proxy would be.
    /// </summary>
    private sealed class ConnectedFrom(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = address;
                    return nextMiddleware(context);
                });

                next(app);
            };
        }
    }
}
