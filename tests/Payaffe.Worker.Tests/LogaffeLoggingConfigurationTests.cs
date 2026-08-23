using Logaffe.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payaffe.Infrastructure.Telemetry;

namespace Payaffe.Worker.Tests;

/// <summary>
/// Where a host's log entries go (ADR 0025). These cover the configuration
/// branches rather than delivery: the client promises nothing about arrival,
/// so what is worth testing is that an installation is never quietly wired to
/// a logaffe it cannot reach.
/// </summary>
public sealed class LogaffeLoggingConfigurationTests
{
    [Fact]
    public void An_installation_without_logaffe_keeps_its_logs_on_the_console()
    {
        using var host = BuildHost([]);

        Assert.DoesNotContain(
            host.Services.GetServices<ILoggerProvider>(),
            provider => provider is LogaffeLoggerProvider);
    }

    [Fact]
    public void An_address_and_a_token_register_delivery()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Observability:Logaffe:Url"] = "https://logs.example.com",
            ["Observability:Logaffe:IngestToken"] = "token-value",
        });

        Assert.Contains(
            host.Services.GetServices<ILoggerProvider>(),
            provider => provider is LogaffeLoggerProvider);
    }

    /// <summary>
    /// Half a configuration is the case worth failing on. A wrong OTLP endpoint
    /// shows up as an empty dashboard, but logs that were never delivered are
    /// missed at the moment somebody needs them.
    /// </summary>
    [Fact]
    public void A_token_without_an_address_fails_at_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new Dictionary<string, string?>
        {
            ["Observability:Logaffe:IngestToken"] = "token-value",
        }));

        Assert.Contains("Observability:Logaffe:Url", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_without_a_token_fails_at_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new Dictionary<string, string?>
        {
            ["Observability:Logaffe:Url"] = "https://logs.example.com",
        }));

        Assert.Contains("Observability:Logaffe:IngestToken", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("logs.example.com")]
    [InlineData("/var/log/payaffe")]
    [InlineData("ftp://logs.example.com")]
    public void An_address_that_is_not_an_http_installation_fails_at_startup(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new Dictionary<string, string?>
        {
            ["Observability:Logaffe:Url"] = url,
            ["Observability:Logaffe:IngestToken"] = "token-value",
        }));

        Assert.Contains(url, error.Message, StringComparison.Ordinal);
    }

    private static IHost BuildHost(IEnumerable<KeyValuePair<string, string?>> configuration)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddPayaffeTelemetry("payaffe-worker");
        return builder.Build();
    }
}
