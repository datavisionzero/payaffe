using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Payaffe.Api.Tests.Diagnostics;

/// <summary>
/// The one endpoint anybody on the internet may post to. What is verified here
/// is the bounding, not the reporting: that a caller cannot choose the shape of
/// what is logged, cannot raise the installation's error rate, and cannot post
/// without limit.
/// </summary>
public sealed class ClientErrorEndpointTests
{
    [Fact]
    public async Task A_reported_error_is_accepted_and_logged_as_a_warning()
    {
        var log = new RecordingLoggerProvider();
        await using var factory = CreateFactory(log);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/client-errors", new
        {
            name = "TypeError",
            message = "x is not a function",
            stack = "TypeError: x is not a function\n    at pay (/pay/abc:1:2)",
            path = "https://pay.example.test/pay/abc?token=secret",
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var entry = Assert.Single(log.Entries, e => e.Category == "Payaffe.Web.ClientError");

        // Warning rather than Error, and that is the security property: an
        // unauthenticated caller must not be able to move the number an alert
        // is derived from.
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(
            "Browser reported TypeError on /pay/abc: x is not a function",
            entry.Message);

        // The query string never reaches the log.
        Assert.DoesNotContain("token=secret", entry.Message, StringComparison.Ordinal);

        // The stack rides in the field a log store keeps a stack trace in, and
        // renders as the text it was given rather than as a .NET exception.
        Assert.NotNull(entry.Exception);
        Assert.StartsWith("TypeError: x is not a function", entry.Exception!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ClientReportedError", entry.Exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The message is a value and never part of the template, so a caller that
    /// reports a placeholder gets a placeholder printed back rather than a
    /// substitution.
    /// </summary>
    [Fact]
    public async Task A_reported_placeholder_is_not_a_placeholder()
    {
        var log = new RecordingLoggerProvider();
        await using var factory = CreateFactory(log);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/client-errors", new
        {
            name = "{ClientErrorPath}",
            message = "{ClientErrorName} settled {NotAThing}",
            path = "/admin",
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var entry = Assert.Single(log.Entries, e => e.Category == "Payaffe.Web.ClientError");
        Assert.Equal(
            "Browser reported {ClientErrorPath} on /admin: {ClientErrorName} settled {NotAThing}",
            entry.Message);
    }

    [Fact]
    public async Task An_oversized_report_is_refused_before_it_is_read()
    {
        var log = new RecordingLoggerProvider();
        await using var factory = CreateFactory(log);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/client-errors", new
        {
            name = "TypeError",
            message = new string('x', 64 * 1024),
            path = "/pay/abc",
        });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.DoesNotContain(log.Entries, e => e.Category == "Payaffe.Web.ClientError");
    }

    [Fact]
    public async Task Reporting_without_limit_is_refused()
    {
        var log = new RecordingLoggerProvider();
        await using var factory = CreateFactory(log, permitLimit: 2);
        using var client = factory.CreateClient();

        var payload = new { name = "TypeError", message = "again", path = "/pay/abc" };
        await client.PostAsJsonAsync("/api/client-errors", payload);
        await client.PostAsJsonAsync("/api/client-errors", payload);
        var third = await client.PostAsJsonAsync("/api/client-errors", payload);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(2, log.Entries.Count(e => e.Category == "Payaffe.Web.ClientError"));
    }

    /// <summary>
    /// Disabled means unmapped. An installation that does not want a publicly
    /// postable surface does not get one that answers and discards.
    /// </summary>
    [Fact]
    public async Task A_disabled_endpoint_does_not_exist()
    {
        var log = new RecordingLoggerProvider();
        await using var factory = CreateFactory(log, enabled: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/client-errors", new { message = "anything" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// It is a diagnostic, not part of what the web app is contracted to call,
    /// so it stays out of the generated description and out of the snapshot the
    /// client is generated from.
    /// </summary>
    [Fact]
    public async Task It_is_absent_from_the_published_contract()
    {
        var log = new RecordingLoggerProvider();
        await using var factory = CreateFactory(log);
        using var client = factory.CreateClient();

        foreach (var document in new[] { "web", "v1" })
        {
            var body = await client.GetStringAsync($"/openapi/{document}.json");
            Assert.DoesNotContain("client-errors", body, StringComparison.Ordinal);
        }
    }

    private static ClientErrorFactory CreateFactory(
        RecordingLoggerProvider log,
        int? permitLimit = null,
        bool enabled = true)
    {
        return new ClientErrorFactory(log, permitLimit, enabled);
    }

    private sealed class ClientErrorFactory(
        RecordingLoggerProvider log,
        int? permitLimit,
        bool enabled) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // Read out of configuration at startup, because a disabled endpoint
            // is never mapped rather than being switched off per request.
            builder.UseSetting("Diagnostics:ClientErrors:Enabled", enabled ? "true" : "false");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(log);
                if (permitLimit is not null)
                {
                    services.Configure<ClientErrorReportOptions>(
                        options => options.RateLimitPermitLimit = permitLimit.Value);
                }
            });
        }
    }

    private sealed record RecordedEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<RecordedEntry> _entries = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<RecordedEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private void Record(RecordedEntry entry)
        {
            lock (_gate)
            {
                _entries.Add(entry);
            }
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                provider.Record(new RecordedEntry(category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}
