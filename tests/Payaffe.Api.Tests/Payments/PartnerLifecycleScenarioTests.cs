using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Payaffe.Api.Tests.Payments;

public sealed class PartnerLifecycleScenarioTests
{
    [Fact]
    public async Task Partner_completes_payment_deduplicates_signed_webhooks_and_reconciles_by_polling()
    {
        const string token = "partner-scenario-token";
        const string webhookSecret = "partner-scenario-webhook-secret";
        const string secretReference = "configuration:Webhooks:EndpointSecrets:partner";
        await using var factory = new PaymentApiFactory();
        var credentialId = await factory.SeedCredentialAsync(token);
        using (var setupScope = factory.Services.CreateScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            dbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
            {
                Id = Guid.NewGuid(),
                IntegrationApiCredentialId = credentialId,
                Url = "https://partner.example.test/hooks/payments",
                SecretReference = secretReference,
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1,
            });
            await dbContext.SaveChangesAsync();
        }

        using var partnerClient = factory.CreateClient();
        partnerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        partnerClient.DefaultRequestHeaders.Add("Idempotency-Key", "partner-order-123");
        var createRequest = new
        {
            fiatCurrency = "EUR",
            fiatAmountMinor = 1999,
            externalReference = "partner-order-123",
        };
        var createdResponse = await partnerClient.PostAsJsonAsync(
            "/api/v1/payments",
            createRequest);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var createdJson = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = createdJson.GetProperty("paymentId").GetGuid();
        var payerPageId = new Uri(createdJson.GetProperty("payerPageUrl").GetString()!)
            .Segments.Last().Trim('/');

        var idempotentResponse = await partnerClient.PostAsJsonAsync(
            "/api/v1/payments",
            createRequest);
        Assert.Equal(HttpStatusCode.OK, idempotentResponse.StatusCode);
        var idempotentJson = await idempotentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(paymentId, idempotentJson.GetProperty("paymentId").GetGuid());

        using var payerClient = factory.CreateClient();
        payerClient.DefaultRequestHeaders.Add("Idempotency-Key", "select-btc");
        var selectionResponse = await payerClient.PostAsJsonAsync(
            $"/api/payer/payments/{payerPageId}/currency-selection",
            new { supportedCurrency = "BTC" });
        selectionResponse.EnsureSuccessStatusCode();

        using (var observationScope = factory.Services.CreateScope())
        {
            var payments = observationScope.ServiceProvider
                .GetRequiredService<PaymentApplicationService>();
            var observation = await payments.RecordBlockchainObservationAsync(
                new RecordBlockchainObservationCommand(
                    paymentId,
                    "BTC",
                    "bc1qpayaffetestaddress0000000000000000000000000",
                    "partner-controlled-transaction",
                    "0.00039980",
                    DateTimeOffset.UtcNow,
                    Confirmations: 1,
                    "controlled-test-provider",
                    "partner-observation-1"),
                CancellationToken.None);
            Assert.Equal(RecordBlockchainObservationResultKind.Completed, observation.Kind);
        }

        var pollingResponse = await partnerClient.GetAsync($"/api/v1/payments/{paymentId:D}");
        pollingResponse.EnsureSuccessStatusCode();
        var reconciled = await pollingResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", reconciled.GetProperty("status").GetString());

        var receiver = new DeduplicatingReceiver(webhookSecret);
        var deliveryClock = new MutableClock(DateTimeOffset.UtcNow.AddMinutes(1));
        using (var deliveryScope = factory.Services.CreateScope())
        {
            var processor = new WebhookDeliveryProcessor(
                deliveryScope.ServiceProvider.GetRequiredService<PayaffeDbContext>(),
                new HttpClient(receiver),
                new FixedWebhookSecretResolver(secretReference, webhookSecret),
                deliveryClock,
                Options.Create(new WebhookDeliveryOptions
                {
                    MaxAttempts = 5,
                    RetryDelay = TimeSpan.FromMinutes(1),
                    RetryJitterRatio = 0,
                }));

            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
            deliveryClock.UtcNow = deliveryClock.UtcNow.AddMinutes(2);
            while (await processor.ProcessNextAsync(CancellationToken.None))
            {
            }
        }

        Assert.True(receiver.RequestCount > receiver.UniqueEventIds.Count);
        Assert.Equal(4, receiver.UniqueEventIds.Count);
        Assert.Equal(4, receiver.ProcessedEventIds.Count);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.All(verifyDbContext.WebhookOutboxEvents, webhook =>
            Assert.Equal("delivered", webhook.Status));
    }

    private sealed class DeduplicatingReceiver(string secret) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public HashSet<Guid> UniqueEventIds { get; } = [];

        public HashSet<Guid> ProcessedEventIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var eventId = document.RootElement.GetProperty("event_id").GetGuid();
            UniqueEventIds.Add(eventId);
            if (ProcessedEventIds.Add(eventId))
            {
                // The receiver performs its business side effect only once.
            }

            var timestamp = long.Parse(
                request.Headers.GetValues("Payaffe-Webhook-Timestamp").Single(),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(
                WebhookSignatureService.CreateSignature(
                    secret,
                    DateTimeOffset.FromUnixTimeSeconds(timestamp),
                    body),
                request.Headers.GetValues("Payaffe-Webhook-Signature").Single());
            return new HttpResponseMessage(RequestCount == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.NoContent);
        }
    }

    private sealed class FixedWebhookSecretResolver(
        string expectedReference,
        string secret) : IWebhookSecretResolver
    {
        public Task<string?> ResolveAsync(
            string secretReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(
                StringComparer.Ordinal.Equals(secretReference, expectedReference)
                    ? secret
                    : null);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
