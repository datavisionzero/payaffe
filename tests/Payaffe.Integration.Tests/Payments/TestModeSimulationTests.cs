using Payaffe.Application;
using System.Text.Json;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Domain.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Payments;

/// <summary>
/// A Test Mode installation end to end below the HTTP surface: no wallet, no
/// provider, fixed rates, and Simulated Transactions that reach every
/// Payment Status through the same lifecycle a real one does (ADR 0033).
/// </summary>
public sealed class TestModeSimulationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("0b3c3a8e-7b0c-4f59-a3b0-6c0b86a0a2f1");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-23T12:00:00Z");

    [Fact]
    public async Task A_simulated_payment_of_the_expected_amount_is_observed_and_then_completed()
    {
        await using var context = await CreateContextAsync();
        var payment = await context.CreateSelectedPaymentAsync("BTC");

        var recorded = await context.Simulation.RecordSimulatedTransactionAsync(
            new RecordSimulatedTransactionCommand(ProjectDefaults.DefaultProjectId, payment.PaymentId, Amount: null),
            CancellationToken.None);

        Assert.Equal(RecordSimulatedTransactionResultKind.Recorded, recorded.Kind);
        Assert.Equal(payment.ExpectedCryptoAmount, recorded.Transaction!.Amount);
        Assert.Equal("waiting_for_payment", await context.StatusAsync(payment.PaymentId));

        await context.PollAsync();
        Assert.Equal("observed", await context.StatusAsync(payment.PaymentId));
        Assert.Equal(0, (await context.TransactionAsync(payment.PaymentId)).Confirmations);

        await context.PollAsync();
        Assert.Equal("completed", await context.StatusAsync(payment.PaymentId));

        var transaction = await context.TransactionAsync(payment.PaymentId);
        Assert.Equal(SimulatedBlockchainObservationAdapter.ProviderName, transaction.ProviderName);
        Assert.Equal(recorded.Transaction.TransactionHash, transaction.TransactionHash);
        Assert.Equal(1 + 6, transaction.Confirmations);
        var events = await context.EventTypesAsync(payment.PaymentId);
        Assert.Contains(PaymentSimulationService.EventType, events);
        Assert.Contains("payment.observed", events);
        Assert.Contains("payment.completed", events);
        var webhooks = await context.WebhookEventTypesAsync(payment.PaymentId);
        Assert.Contains("payment.observed", webhooks);
        Assert.Contains("payment.completed", webhooks);
        Assert.DoesNotContain(PaymentSimulationService.EventType, webhooks);
    }

    [Theory]
    [InlineData("LTC", "0.99", "completed")]
    [InlineData("LTC", "0.5", "observed")]
    [InlineData("ETH", "1.5", "completed")]
    public async Task A_simulated_amount_is_judged_like_a_real_one(string currency, string fraction, string expectedStatus)
    {
        await using var context = await CreateContextAsync();
        var payment = await context.CreateSelectedPaymentAsync(currency);
        var amount = (decimal.Parse(payment.ExpectedCryptoAmount!, System.Globalization.CultureInfo.InvariantCulture) *
                      decimal.Parse(fraction, System.Globalization.CultureInfo.InvariantCulture))
            .ToString(currency == "ETH" ? "0.##################" : "0.########", System.Globalization.CultureInfo.InvariantCulture);

        await context.Simulation.RecordSimulatedTransactionAsync(
            new RecordSimulatedTransactionCommand(ProjectDefaults.DefaultProjectId, payment.PaymentId, amount),
            CancellationToken.None);
        await context.PollAsync();
        await context.PollAsync();

        Assert.Equal(expectedStatus, await context.StatusAsync(payment.PaymentId));
        Assert.Equal(amount, (await context.TransactionAsync(payment.PaymentId)).ObservedAmount);
    }

    [Fact]
    public async Task A_top_up_after_an_underpayment_completes_the_payment()
    {
        await using var context = await CreateContextAsync();
        var payment = await context.CreateSelectedPaymentAsync("BTC");
        var half = (decimal.Parse(payment.ExpectedCryptoAmount!, System.Globalization.CultureInfo.InvariantCulture) / 2m)
            .ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

        await context.RecordAsync(payment.PaymentId, half);
        await context.PollAsync();
        await context.PollAsync();
        Assert.Equal("observed", await context.StatusAsync(payment.PaymentId));

        await context.RecordAsync(payment.PaymentId, payment.ExpectedCryptoAmount);
        await context.PollAsync();
        await context.PollAsync();

        Assert.Equal("completed", await context.StatusAsync(payment.PaymentId));
    }

    [Fact]
    public async Task A_simulated_payment_after_expiration_is_accepted_inside_the_late_window()
    {
        await using var context = await CreateContextAsync();
        var payment = await context.CreateSelectedPaymentAsync("BTC");
        context.Clock.UtcNow = payment.ExpiresAt.AddMinutes(5);

        await context.RecordAsync(payment.PaymentId, amount: null);
        await context.PollAsync();
        await context.PollAsync();

        Assert.Equal("completed", await context.StatusAsync(payment.PaymentId));
    }

    [Fact]
    public async Task Only_a_payment_waiting_for_its_money_can_receive_a_simulated_transaction()
    {
        await using var context = await CreateContextAsync();
        var unselected = await context.CreatePaymentAsync("unselected");
        var completed = await context.CreateSelectedPaymentAsync("BTC");
        await context.RecordAsync(completed.PaymentId, amount: null);
        await context.PollAsync();
        await context.PollAsync();

        var beforeSelection = await context.Simulation.RecordSimulatedTransactionAsync(
            new RecordSimulatedTransactionCommand(ProjectDefaults.DefaultProjectId, unselected.PaymentId, null),
            CancellationToken.None);
        var afterCompletion = await context.Simulation.RecordSimulatedTransactionAsync(
            new RecordSimulatedTransactionCommand(ProjectDefaults.DefaultProjectId, completed.PaymentId, null),
            CancellationToken.None);
        var otherProject = await context.Simulation.RecordSimulatedTransactionAsync(
            new RecordSimulatedTransactionCommand(Guid.NewGuid(), completed.PaymentId, null),
            CancellationToken.None);
        var unknown = await context.Simulation.RecordSimulatedTransactionAsync(
            new RecordSimulatedTransactionCommand(ProjectDefaults.DefaultProjectId, Guid.NewGuid(), null),
            CancellationToken.None);

        Assert.Equal(RecordSimulatedTransactionResultKind.PaymentNotReady, beforeSelection.Kind);
        Assert.Equal(RecordSimulatedTransactionResultKind.PaymentNotReady, afterCompletion.Kind);
        Assert.Equal(RecordSimulatedTransactionResultKind.PaymentNotFound, otherProject.Kind);
        Assert.Equal(RecordSimulatedTransactionResultKind.PaymentNotFound, unknown.Kind);
    }

    [Theory]
    [InlineData("0", "amount.not_positive")]
    [InlineData("-1", "amount.invalid")]
    [InlineData("0.000000001", "amount.invalid")]
    [InlineData("1e-3", "amount.invalid")]
    public async Task An_amount_that_is_not_a_positive_currency_amount_is_rejected(string amount, string code)
    {
        await using var context = await CreateContextAsync();
        var payment = await context.CreateSelectedPaymentAsync("BTC");

        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => context.RecordAsync(payment.PaymentId, amount));

        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task Every_delivered_webhook_says_it_comes_from_a_test_installation()
    {
        await using var context = await CreateContextAsync();
        await context.AddWebhookEndpointAsync();
        var payment = await context.CreateSelectedPaymentAsync("BTC");
        await context.RecordAsync(payment.PaymentId, amount: null);
        await context.PollAsync();
        await context.PollAsync();

        var bodies = await context.DeliverAllWebhooksAsync();

        Assert.Equal(
            ["payment.completed", "payment.created", "payment.currency_selected", "payment.observed"],
            bodies.Select(body => body.RootElement.GetProperty("event_type").GetString()).Order(StringComparer.Ordinal));
        Assert.All(bodies, body => Assert.True(body.RootElement.GetProperty("test_mode").GetBoolean()));
        Assert.True(payment.TestMode);
    }

    [Fact]
    public async Task A_live_installation_has_no_simulation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(await postgres.CreateDatabaseAsync());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.Null(scope.ServiceProvider.GetService<PaymentSimulationService>());
        Assert.Null(scope.ServiceProvider.GetService<SimulatedBlockchainObservationAdapter>());
        Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IBlockchainObservationAdapterResolver>().Resolve("simulated"));
    }

    private async Task<SimulationContext> CreateContextAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Installation:Mode"] = "test" })
            .Build();
        var clock = new MutableClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPayaffeInstallationMode(configuration);
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton(clock);
        services.AddSingleton<IClock>(clock);
        var webhookReceiver = new CapturingHttpMessageHandler();
        services.AddSingleton(webhookReceiver);
        services.AddHttpClient<WebhookDeliveryProcessor>()
            .ConfigurePrimaryHttpMessageHandler(() => webhookReceiver);
        services.AddScoped<IWebhookSecretResolver, FixedWebhookSecretResolver>();
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PaymentExpiration = TimeSpan.FromMinutes(30);
            options.LateAcceptanceWindow = TimeSpan.FromMinutes(30);
        });
        var provider = services.BuildServiceProvider();
        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("simulation-token"),
            Status = "active",
            CreatedAt = Start,
            UpdatedAt = Start,
        });
        await dbContext.SaveChangesAsync();
        return new SimulationContext(provider, scope, clock);
    }

    private sealed class SimulationContext(ServiceProvider provider, AsyncServiceScope scope, MutableClock clock)
        : IAsyncDisposable
    {
        public MutableClock Clock { get; } = clock;

        public PaymentSimulationService Simulation =>
            scope.ServiceProvider.GetRequiredService<PaymentSimulationService>();

        private PaymentApplicationService Payments =>
            scope.ServiceProvider.GetRequiredService<PaymentApplicationService>();

        private PayaffeDbContext DbContext => scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();

        public async Task<PaymentResponse> CreatePaymentAsync(string reference)
        {
            var created = await Payments.CreateAsync(
                CredentialId,
                new CreatePaymentCommand("EUR", 1999, reference, PaymentContext: null, ReturnUrl: null, $"key-{reference}"),
                CancellationToken.None);
            return created.Payment!;
        }

        public async Task<PaymentResponse> CreateSelectedPaymentAsync(string currency)
        {
            var created = await CreatePaymentAsync($"order-{Guid.NewGuid():N}");
            var payerPageId = created.PayerPageUrl[(created.PayerPageUrl.LastIndexOf('/') + 1)..];
            var selected = await Payments.SelectCurrencyAsync(
                new SelectPaymentCurrencyCommand(payerPageId, currency),
                CancellationToken.None);
            Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selected.Kind);
            return selected.Payment!;
        }

        public async Task AddWebhookEndpointAsync()
        {
            DbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
            {
                Id = Guid.NewGuid(),
                IntegrationApiCredentialId = CredentialId,
                Url = "https://receiver.example.test/webhooks/payaffe",
                SecretReference = "secret://webhooks/test-endpoint",
                Status = "active",
                EventTypes = null,
                CreatedAt = Start,
                UpdatedAt = Start,
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<JsonDocument>> DeliverAllWebhooksAsync()
        {
            await using var deliveryScope = provider.CreateAsyncScope();
            var processor = deliveryScope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
            while (await processor.ProcessNextAsync(CancellationToken.None))
            {
            }

            return provider.GetRequiredService<CapturingHttpMessageHandler>().Bodies
                .Select(body => JsonDocument.Parse(body))
                .ToArray();
        }

        public Task<RecordSimulatedTransactionResult> RecordAsync(Guid paymentId, string? amount) =>
            Simulation.RecordSimulatedTransactionAsync(
                new RecordSimulatedTransactionCommand(ProjectDefaults.DefaultProjectId, paymentId, amount),
                CancellationToken.None);

        /// <summary>A poll in its own scope, as the worker runs one.</summary>
        public async Task PollAsync()
        {
            await using var pollScope = provider.CreateAsyncScope();
            var result = await pollScope.ServiceProvider.GetRequiredService<PaymentApplicationService>()
                .PollBlockchainObservationsAsync(25, CancellationToken.None);
            Assert.Equal(0, result.FailedCount);
        }

        public async Task<string> StatusAsync(Guid paymentId)
        {
            DbContext.ChangeTracker.Clear();
            return (await DbContext.Payments.SingleAsync(payment => payment.Id == paymentId)).Status;
        }

        public async Task<MatchingBlockchainTransactionRecord> TransactionAsync(Guid paymentId)
        {
            DbContext.ChangeTracker.Clear();
            return await DbContext.MatchingBlockchainTransactions
                .OrderBy(transaction => transaction.CreatedAt)
                .FirstAsync(transaction => transaction.PaymentId == paymentId);
        }

        public async Task<IReadOnlyList<string>> EventTypesAsync(Guid paymentId) =>
            await DbContext.PaymentEventHistory
                .Where(paymentEvent => paymentEvent.PaymentId == paymentId)
                .Select(paymentEvent => paymentEvent.EventType)
                .ToListAsync();

        public async Task<IReadOnlyList<string>> WebhookEventTypesAsync(Guid paymentId) =>
            await DbContext.WebhookOutboxEvents
                .Where(webhookEvent => webhookEvent.PaymentId == paymentId)
                .Select(webhookEvent => webhookEvent.EventType)
                .ToListAsync();

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }

    private sealed class FixedWebhookSecretResolver : IWebhookSecretResolver
    {
        public Task<string?> ResolveForProjectAsync(Guid projectId, string secretReference, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("test-webhook-secret");
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Start;
    }
}
