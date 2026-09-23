using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Webhooks;
using Payaffe.Sdk;

namespace Payaffe.Api.Tests.Payments;

/// <summary>
/// The embedded acceptance proof: a target product's backend drives a Payment from creation
/// to completion through the SDK alone. No request reaches the hosted Payer Page or the
/// Payer routes, so an integration that never renders `payerPageUrl` still works.
/// </summary>
public sealed class EmbeddedPaymentFlowTests
{
    private const string _token = "embedded-flow-token";
    private const string _webhookSecret = "embedded-flow-webhook-secret";
    private const string _secretReference = "configuration:Webhooks:EndpointSecrets:embedded";

    [Fact]
    public async Task A_product_backend_completes_a_payment_without_visiting_the_payer_page()
    {
        await using PaymentApiFactory factory = new();
        Guid credentialId = await factory.SeedCredentialAsync(_token);
        factory.AddWebhookSecret(_secretReference, _webhookSecret);
        await SeedWebhookEndpointAsync(factory, credentialId);

        RecordingHandler recorder = new();
        using HttpClient httpClient = factory.CreateDefaultClient(recorder);
        PayaffeClient payaffe = new(httpClient, new Uri("http://localhost"), _token);

        Payment created = await payaffe.CreatePaymentAsync(
            new CreatePaymentRequest("EUR", 1999, "embedded-order-1"),
            idempotencyKey: "embedded-order-1-attempt-1");
        Assert.Equal(PaymentStatus.PendingCurrencySelection, created.Status);
        Assert.All(created.PaymentOptions, option =>
            Assert.Equal(PaymentOptionStatus.Available, option.Status));

        Payment selected = await payaffe.SelectCurrencyAsync(
            created.PaymentId,
            SupportedCurrency.Btc);
        PaymentInstruction instruction = Assert.IsType<PaymentInstruction>(selected.PaymentInstruction);
        Assert.Equal("0.00039980", instruction.Amount);
        Assert.Equal("39980", instruction.AmountAtomic);
        Assert.Equal("bc1qpayaffetestaddress0000000000000000000000000", instruction.PaymentAddress);
        Assert.StartsWith("bitcoin:", instruction.Uri, StringComparison.Ordinal);

        // The product's own UI renders these three values; nothing else is needed to pay.
        Assert.Equal(instruction.PaymentAddress, selected.PaymentAddress);
        Assert.Equal(instruction.Amount, selected.ExpectedCryptoAmount);

        List<PaymentStatus> polledStates = [];
        bool observationRecorded = false;
        await foreach (Payment state in payaffe.PollPaymentAsync(
            created.PaymentId,
            new PaymentPollingOptions
            {
                InitialInterval = TimeSpan.FromMilliseconds(10),
                MaximumInterval = TimeSpan.FromMilliseconds(20),
            }))
        {
            polledStates.Add(state.Status);
            if (!observationRecorded)
            {
                await RecordObservationAsync(factory, created.PaymentId, instruction, "0.00039980");
                observationRecorded = true;
            }
        }

        Assert.Equal(PaymentStatus.WaitingForPayment, polledStates[0]);
        Assert.Equal(PaymentStatus.Completed, polledStates[^1]);

        Payment reconciled = await payaffe.GetPaymentAsync(created.PaymentId);
        Assert.Equal(PaymentStatus.Completed, reconciled.Status);
        Assert.Equal("exact", reconciled.ObservedAmountState);
        Assert.NotNull(reconciled.CompletedAt);

        VerifyingReceiver receiver = new();
        await DeliverWebhooksAsync(factory, receiver);

        Assert.All(receiver.Rejections, rejection => Assert.Null(rejection));
        Assert.Equal(
            [
                PayaffeWebhookEventTypes.PaymentCreated,
                PayaffeWebhookEventTypes.PaymentCurrencySelected,
                PayaffeWebhookEventTypes.PaymentObserved,
                PayaffeWebhookEventTypes.PaymentCompleted,
            ],
            receiver.VerifiedEvents.Select(verified => verified.EventType));
        PayaffeWebhookEvent completedEvent = receiver.VerifiedEvents.Single(verified =>
            verified.EventType == PayaffeWebhookEventTypes.PaymentCompleted);
        Assert.Equal(created.PaymentId, completedEvent.Payment.PaymentId);
        Assert.Equal("embedded-order-1", completedEvent.Payment.ExternalReference);
        Assert.Equal(PaymentStatus.Completed, completedEvent.Payment.Status);
        Assert.Equal("39980", completedEvent.Payment.ExpectedCryptoAmountAtomic);

        // A Delivery that another endpoint's secret would have accepted is not verifiable here.
        Assert.False(receiver.LastDeliveryVerifiesWithForeignSecret);

        Assert.NotEmpty(recorder.RequestedPaths);
        Assert.All(recorder.RequestedPaths, path =>
            Assert.StartsWith("/api/v1/payments", path, StringComparison.Ordinal));
        Assert.StartsWith("https://pay.example.test/pay/", created.PayerPageUrl, StringComparison.Ordinal);
    }

    private static async Task SeedWebhookEndpointAsync(PaymentApiFactory factory, Guid credentialId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        PayaffeDbContext dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        dbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
        {
            Id = Guid.NewGuid(),
            IntegrationApiCredentialId = credentialId,
            Url = "https://shop.example.test/hooks/payaffe",
            SecretReference = _secretReference,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task RecordObservationAsync(
        PaymentApiFactory factory,
        Guid paymentId,
        PaymentInstruction instruction,
        string observedAmount)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        PaymentApplicationService payments = scope.ServiceProvider
            .GetRequiredService<PaymentApplicationService>();
        RecordBlockchainObservationResult result = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                paymentId,
                instruction.SupportedCurrency.Value,
                instruction.PaymentAddress,
                $"embedded-transaction-{observedAmount}",
                observedAmount,
                DateTimeOffset.UtcNow,
                Confirmations: 1,
                "controlled-test-provider",
                $"embedded-observation-{observedAmount}",
                ProjectId: ProjectDefaults.DefaultProjectId),
            CancellationToken.None);
        Assert.Contains(
            result.Kind,
            new[]
            {
                RecordBlockchainObservationResultKind.Observed,
                RecordBlockchainObservationResultKind.Completed,
            });
    }

    private static async Task DeliverWebhooksAsync(
        PaymentApiFactory factory,
        VerifyingReceiver receiver)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        WebhookDeliveryProcessor processor = new(
            scope.ServiceProvider.GetRequiredService<PayaffeDbContext>(),
            new HttpClient(receiver),
            scope.ServiceProvider.GetRequiredService<IWebhookSecretResolver>(),
            scope.ServiceProvider.GetRequiredService<IClock>(),
            Options.Create(new WebhookDeliveryOptions
            {
                MaxAttempts = 5,
                RetryDelay = TimeSpan.FromMinutes(1),
                RetryJitterRatio = 0,
            }));

        while (await processor.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedPaths.Add(request.RequestUri!.AbsolutePath);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A receiving backend as the SDK documentation describes one: verify the raw bytes,
    /// deduplicate by event identifier, and only then act on the event.
    /// </summary>
    private sealed class VerifyingReceiver : HttpMessageHandler
    {
        private readonly HashSet<Guid> _seenEventIds = [];

        public List<PayaffeWebhookEvent> VerifiedEvents { get; } = [];

        public List<PayaffeWebhookRejectionReason?> Rejections { get; } = [];

        public bool LastDeliveryVerifiesWithForeignSecret { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            PayaffeWebhookHeaders headers = PayaffeWebhookHeaders.FromLookup(name =>
                request.Headers.TryGetValues(name, out IEnumerable<string>? values)
                    ? values.FirstOrDefault()
                    : null);

            PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
                body,
                headers,
                _webhookSecret,
                DateTimeOffset.UtcNow);
            Rejections.Add(result.RejectionReason);
            LastDeliveryVerifiesWithForeignSecret = PayaffeWebhookVerifier
                .Verify(body, headers, "another-endpoints-secret", DateTimeOffset.UtcNow)
                .IsValid;

            if (result.Event is { } verified && _seenEventIds.Add(verified.EventId))
            {
                VerifiedEvents.Add(verified);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }
}
