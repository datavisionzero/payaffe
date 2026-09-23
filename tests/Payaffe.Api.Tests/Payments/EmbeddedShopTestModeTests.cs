extern alias shop;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Webhooks;
using ShopEntryPoint = shop::EmbeddedShop.ShopOptions;

namespace Payaffe.Api.Tests.Payments;

/// <summary>
/// The acceptance proof of Test Mode (ADR 0033): the embedded checkout sample, unchanged,
/// against a real Test Mode installation with no wallet, no provider and no rate source. The
/// shop places orders and simulates the payment through the SDK; the installation observes the
/// simulated transfer on its own worker path and delivers signed webhooks to the shop, which
/// decides from them alone.
/// </summary>
public sealed class EmbeddedShopTestModeTests
{
    private const string Storefront = "teahouse";
    private const string Token = "teahouse-test-token";
    private const string WebhookSecret = "teahouse-test-webhook-secret";
    private const string SecretReference = "configuration:Webhooks:EndpointSecrets:teahouse";

    [Fact]
    public async Task An_order_paid_with_the_expected_amount_is_fulfilled()
    {
        await using TestInstallation installation = await TestInstallation.StartAsync();
        Guid orderId = await installation.PlaceOrderWithInstructionAsync();

        await installation.SimulateAsync(orderId, amount: null);
        await installation.RunWorkersAsync();

        JsonElement order = await installation.ReadOrderAsync(orderId);
        Assert.Equal("Fulfilled", order.GetProperty("fulfillment").GetString());
        Assert.Equal("completed", order.GetProperty("paymentState").GetString());
        Assert.True(order.GetProperty("testMode").GetBoolean());
        Assert.Contains("payment.completed", installation.DeliveredEventTypes);
        Assert.All(installation.DeliveredTestModeFlags, Assert.True);
        Assert.All(installation.DeliveryStatuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }

    [Fact]
    public async Task An_underpaid_order_is_not_fulfilled()
    {
        await using TestInstallation installation = await TestInstallation.StartAsync();
        Guid orderId = await installation.PlaceOrderWithInstructionAsync();
        string half = await installation.ScaledExpectedAmountAsync(orderId, 0.5m);

        await installation.SimulateAsync(orderId, half);
        await installation.RunWorkersAsync();

        JsonElement order = await installation.ReadOrderAsync(orderId);
        Assert.Equal("AwaitingPayment", order.GetProperty("fulfillment").GetString());
        Assert.Equal("observed", order.GetProperty("paymentState").GetString());
        Assert.Contains("payment.observed", installation.DeliveredEventTypes);
        Assert.DoesNotContain("payment.completed", installation.DeliveredEventTypes);
    }

    [Fact]
    public async Task An_overpaid_order_is_fulfilled()
    {
        await using TestInstallation installation = await TestInstallation.StartAsync();
        Guid orderId = await installation.PlaceOrderWithInstructionAsync();
        string more = await installation.ScaledExpectedAmountAsync(orderId, 1.5m);

        await installation.SimulateAsync(orderId, more);
        await installation.RunWorkersAsync();

        JsonElement order = await installation.ReadOrderAsync(orderId);
        Assert.Equal("Fulfilled", order.GetProperty("fulfillment").GetString());
        Assert.Equal("overpaid", await installation.ObservedAmountStateAsync(orderId));
    }

    [Fact]
    public async Task An_order_nobody_pays_expires_and_stays_open()
    {
        await using TestInstallation installation = await TestInstallation.StartAsync();
        Guid orderId = await installation.PlaceOrderWithInstructionAsync();

        await installation.ExpireAsync(orderId);
        await installation.RunWorkersAsync();

        JsonElement order = await installation.ReadOrderAsync(orderId);
        Assert.Equal("Expired", order.GetProperty("fulfillment").GetString());
        Assert.Equal("expired", order.GetProperty("paymentState").GetString());
        Assert.Contains("payment.expired", installation.DeliveredEventTypes);
    }

    [Fact]
    public async Task A_shop_pointed_at_a_live_installation_cannot_simulate()
    {
        await using TestInstallation installation = await TestInstallation.StartAsync(testMode: false);
        Guid orderId = await installation.PlaceOrderWithInstructionAsync();

        using HttpResponseMessage response = await installation.Browser.PostAsJsonAsync(
            $"/{Storefront}/api/orders/{orderId}/simulated-payment",
            new { amount = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("payaffe_not_in_test_mode", body.RootElement.GetProperty("error").GetString());
    }

    private sealed class TestInstallation : IAsyncDisposable
    {
        private readonly PaymentApiFactory _payaffe;
        private readonly WebApplicationFactory<ShopEntryPoint> _shop;
        private readonly RecordingHandler _webhookTap;

        private TestInstallation(PaymentApiFactory payaffe, WebApplicationFactory<ShopEntryPoint> shopFactory)
        {
            _payaffe = payaffe;
            _shop = shopFactory;
            _webhookTap = new RecordingHandler(_shop.Server.CreateHandler());
            Browser = _shop.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        }

        public HttpClient Browser { get; }

        public IReadOnlyList<string?> DeliveredEventTypes =>
            [.. _webhookTap.Bodies.Select(body => body.RootElement.GetProperty("event_type").GetString())];

        public IReadOnlyList<bool> DeliveredTestModeFlags =>
            [.. _webhookTap.Bodies.Select(body => body.RootElement.GetProperty("test_mode").GetBoolean())];

        public IReadOnlyList<HttpStatusCode> DeliveryStatuses => _webhookTap.Statuses;

        public static async Task<TestInstallation> StartAsync(bool testMode = true)
        {
            PaymentApiFactory payaffe = new() { TestMode = testMode };
            Guid credentialId = await payaffe.SeedCredentialAsync(Token);
            payaffe.AddWebhookSecret(SecretReference, WebhookSecret);
            using (IServiceScope scope = payaffe.Services.CreateScope())
            {
                PayaffeDbContext dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                dbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
                {
                    Id = Guid.NewGuid(),
                    IntegrationApiCredentialId = credentialId,
                    Url = $"https://shop.example.test/{Storefront}/webhooks/payaffe",
                    SecretReference = SecretReference,
                    Status = "active",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                });
                await dbContext.SaveChangesAsync();
            }

            HttpMessageHandler payaffeTransport = payaffe.Server.CreateHandler();
            WebApplicationFactory<ShopEntryPoint> shopFactory = new WebApplicationFactory<ShopEntryPoint>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("Shop:ReconcileByPolling", "false");
                    foreach (string storefront in new[] { Storefront, "roastery" })
                    {
                        builder.UseSetting($"Shop:Storefronts:{storefront}:PayaffeBaseAddress", "http://payaffe.example.test");
                        builder.UseSetting($"Shop:Storefronts:{storefront}:PayaffeApiToken", Token);
                        builder.UseSetting($"Shop:Storefronts:{storefront}:WebhookSecret", WebhookSecret);
                    }

                    builder.UseSetting($"Shop:Storefronts:{Storefront}:AcceptTestPayments", "true");
                    builder.ConfigureServices(services =>
                        services.ConfigureAll<HttpClientFactoryOptions>(options =>
                            options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                                handlerBuilder.PrimaryHandler = payaffeTransport)));
                });

            return new TestInstallation(payaffe, shopFactory);
        }

        public async Task<Guid> PlaceOrderWithInstructionAsync()
        {
            using HttpResponseMessage placed = await Browser.PostAsJsonAsync(
                $"/{Storefront}/api/orders",
                new { sku = "mug-01" });
            placed.EnsureSuccessStatusCode();
            Guid orderId = (await placed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();

            using HttpResponseMessage selected = await Browser.PostAsJsonAsync(
                $"/{Storefront}/api/orders/{orderId}/currency",
                new { currency = "BTC" });
            selected.EnsureSuccessStatusCode();
            return orderId;
        }

        public async Task SimulateAsync(Guid orderId, string? amount)
        {
            using HttpResponseMessage response = await Browser.PostAsJsonAsync(
                $"/{Storefront}/api/orders/{orderId}/simulated-payment",
                new { amount });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public async Task<string> ScaledExpectedAmountAsync(Guid orderId, decimal factor)
        {
            string expected = (await ReadOrderAsync(orderId)).GetProperty("instruction").GetProperty("amount").GetString()!;
            return decimal.Round(
                    decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture) * factor,
                    8,
                    MidpointRounding.ToZero)
                .ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task<JsonElement> ReadOrderAsync(Guid orderId) =>
            await Browser.GetFromJsonAsync<JsonElement>($"/{Storefront}/api/orders/{orderId}");

        public async Task<string?> ObservedAmountStateAsync(Guid orderId)
        {
            using IServiceScope scope = _payaffe.Services.CreateScope();
            PaymentApplicationService payments = scope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
            PayaffeDbContext dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            PaymentRecord record = await dbContext.Payments.SingleAsync(
                payment => payment.ExternalReference == orderId.ToString("D"));
            PaymentResponse? payment = await payments.FindAsync(record.IntegrationApiCredentialId, record.Id, CancellationToken.None);
            return payment!.ObservedAmountState;
        }

        /// <summary>Puts the Payment past its Late Acceptance Window, as time would.</summary>
        public async Task ExpireAsync(Guid orderId)
        {
            using IServiceScope scope = _payaffe.Services.CreateScope();
            PayaffeDbContext dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            PaymentRecord record = await dbContext.Payments.SingleAsync(
                payment => payment.ExternalReference == orderId.ToString("D"));
            record.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-25);
            record.LateAcceptanceEndsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// What the worker host does on its schedule: expire, observe twice (the simulated
        /// transfer is unconfirmed on the first poll and confirmed on the second), and deliver.
        /// </summary>
        public async Task RunWorkersAsync()
        {
            using IServiceScope scope = _payaffe.Services.CreateScope();
            PaymentApplicationService payments = scope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
            await payments.ExpireDuePaymentsAsync(25, CancellationToken.None);
            await payments.PollBlockchainObservationsAsync(25, CancellationToken.None);
            await payments.PollBlockchainObservationsAsync(25, CancellationToken.None);

            WebhookDeliveryProcessor processor = new(
                scope.ServiceProvider.GetRequiredService<PayaffeDbContext>(),
                new HttpClient(_webhookTap),
                scope.ServiceProvider.GetRequiredService<IWebhookSecretResolver>(),
                scope.ServiceProvider.GetRequiredService<IClock>(),
                Options.Create(new WebhookDeliveryOptions
                {
                    MaxAttempts = 5,
                    RetryDelay = TimeSpan.FromMinutes(1),
                    RetryJitterRatio = 0,
                }),
                scope.ServiceProvider.GetRequiredService<Payaffe.Application.Installation.ConfiguredInstallationMode>());
            while (await processor.ProcessNextAsync(CancellationToken.None))
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            Browser.Dispose();
            await _shop.DisposeAsync();
            await _payaffe.DisposeAsync();
        }
    }

    /// <summary>Forwards Payaffe's webhook deliveries to the shop and keeps what was sent.</summary>
    private sealed class RecordingHandler(HttpMessageHandler shop) : DelegatingHandler(shop)
    {
        public List<JsonDocument> Bodies { get; } = [];

        public List<HttpStatusCode> Statuses { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)));
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            Statuses.Add(response.StatusCode);
            return response;
        }
    }
}
