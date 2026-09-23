using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Payaffe.Sdk;

namespace EmbeddedShop.Tests;

/// <summary>
/// The sample shop as it runs, with one substitution: the transport underneath the SDK is the
/// fake Payaffe instead of a network. Everything above it — configuration, DI, routing, the
/// storefronts, the reconciler — is the application's own.
/// </summary>
internal sealed class ShopApplication : WebApplicationFactory<Program>
{
    public const string Teahouse = "teahouse";
    public const string Roastery = "roastery";
    public const string TeahouseWebhookSecret = "teahouse-webhook-secret";
    public const string RoasteryWebhookSecret = "roastery-webhook-secret";

    public FakePayaffe Payaffe { get; } = new();

    /// <summary>
    /// Off by default so a test observes only what it triggered. The one test about the polling
    /// channel turns it on.
    /// </summary>
    public bool ReconcileByPolling { get; init; }

    /// <summary>
    /// Sets the teahouse up the way a developer runs it against a Test Mode installation.
    /// </summary>
    public bool TeahouseAcceptsTestPayments { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Shop:ReconcileByPolling", ReconcileByPolling ? "true" : "false");
        builder.UseSetting($"Shop:Storefronts:{Teahouse}:PayaffeBaseAddress", "http://payaffe.invalid");
        builder.UseSetting($"Shop:Storefronts:{Teahouse}:PayaffeApiToken", FakePayaffe.TeahouseToken);
        builder.UseSetting($"Shop:Storefronts:{Teahouse}:WebhookSecret", TeahouseWebhookSecret);
        builder.UseSetting(
            $"Shop:Storefronts:{Teahouse}:AcceptTestPayments",
            TeahouseAcceptsTestPayments ? "true" : "false");
        builder.UseSetting($"Shop:Storefronts:{Roastery}:PayaffeBaseAddress", "http://payaffe-two.invalid");
        builder.UseSetting($"Shop:Storefronts:{Roastery}:PayaffeApiToken", FakePayaffe.RoasteryToken);
        builder.UseSetting($"Shop:Storefronts:{Roastery}:WebhookSecret", RoasteryWebhookSecret);

        builder.ConfigureServices(services =>
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                    handlerBuilder.PrimaryHandler = Payaffe)));
    }

    /// <summary>
    /// One browser: its own cookie jar, and therefore its own customer.
    /// </summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    /// <summary>
    /// A Webhook Delivery as Payaffe would send it: signed over the timestamp and the exact bytes
    /// of the body, with the headers the contract names.
    /// </summary>
    public static HttpRequestMessage Delivery(
        string storefront,
        string secret,
        Guid eventId,
        string eventType,
        Guid paymentId,
        Guid externalReference,
        string status,
        DateTimeOffset? signedAt = null,
        bool testMode = false)
    {
        DateTimeOffset at = signedAt ?? DateTimeOffset.UtcNow;
        string body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["event_type"] = eventType,
            ["event_version"] = "2026-01-01",
            ["occurred_at"] = at,
            ["correlation_id"] = "test-correlation",
            ["test_mode"] = testMode,
            ["resource"] = new Dictionary<string, object?>
            {
                ["type"] = "payment",
                ["id"] = paymentId,
            },
            ["payment"] = new Dictionary<string, object?>
            {
                ["payment_id"] = paymentId,
                ["external_reference"] = externalReference.ToString("D"),
                ["status"] = status,
                ["fiat_currency"] = "EUR",
                ["fiat_amount_minor"] = 1999,
                ["selected_currency"] = "BTC",
                ["expected_crypto_amount"] = "0.00039980",
                ["expected_crypto_amount_atomic"] = "39980",
                ["observed_total"] = "0.00039980",
                ["confirmed_eligible_total"] = "0.00039980",
                ["observed_amount_state"] = "exact",
                ["payer_page_id"] = null,
                ["expires_at"] = at.AddMinutes(30),
                ["completed_at"] = status == "completed" ? at : null,
                ["settled_at"] = null,
            },
        });

        long timestamp = at.ToUnixTimeSeconds();
        string signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

        HttpRequestMessage request = new(HttpMethod.Post, $"/{storefront}/webhooks/payaffe")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(PayaffeWebhookHeaderNames.DeliveryId, Guid.CreateVersion7().ToString("D"));
        request.Headers.Add(PayaffeWebhookHeaderNames.Timestamp, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(PayaffeWebhookHeaderNames.Signature, $"v1={signature}");
        request.Headers.Add(PayaffeWebhookHeaderNames.EventType, eventType);
        request.Headers.Add(PayaffeWebhookHeaderNames.EventVersion, "2026-01-01");
        return request;
    }
}
