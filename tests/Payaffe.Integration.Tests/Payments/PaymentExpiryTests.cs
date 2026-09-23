using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Payments;

/// <summary>
/// When each kind of active Payment expires (ADR 0034). The clock is moved by
/// hand, and the expiry and observation workers are driven one run at a time.
/// </summary>
public sealed class PaymentExpiryTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("5b0f5d53-3a39-4d47-9d52-3cf3c8f1d0a2");
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
    private const string PaymentAddress = "bc1qpayaffeexpiryaddress00000000000000000000000";

    [Fact]
    public async Task A_payment_without_a_selected_currency_expires_at_payment_expiration()
    {
        await using var context = await BuildContextAsync();
        var payment = await CreatePaymentAsync(context, selectCurrency: false);

        context.Clock.UtcNow = payment.ExpiresAt.AddSeconds(-1);
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);
        Assert.Equal("pending_currency_selection", StatusOf(context, payment.PaymentId));

        context.Clock.UtcNow = payment.ExpiresAt;
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);

        Assert.True(payment.LateAcceptanceEndsAt > context.Clock.UtcNow);
        Assert.Equal("expired", StatusOf(context, payment.PaymentId));
    }

    [Fact]
    public async Task A_payment_waiting_for_payment_still_expires_when_the_late_acceptance_window_ends()
    {
        await using var context = await BuildContextAsync();
        var payment = await CreatePaymentAsync(context);

        context.Clock.UtcNow = payment.ExpiresAt;
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);
        Assert.Equal("waiting_for_payment", StatusOf(context, payment.PaymentId));

        context.Clock.UtcNow = payment.LateAcceptanceEndsAt;
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);

        Assert.Equal("expired", StatusOf(context, payment.PaymentId));
    }

    /// <summary>
    /// A transaction broadcast shortly before the deadline can sit in the
    /// mempool past the window. It was seen in time, so it still counts, and
    /// the Payment must still be there to complete when it confirms.
    /// </summary>
    [Fact]
    public async Task An_observed_payment_completes_when_its_transaction_confirms_after_the_late_acceptance_window()
    {
        await using var context = await BuildContextAsync();
        var payment = await CreatePaymentAsync(context);
        var seenAt = payment.LateAcceptanceEndsAt.AddMinutes(-5);
        context.Adapter.Observations = [Observation(seenAt, confirmations: 0)];
        context.Clock.UtcNow = seenAt;
        await context.Payments.PollBlockchainObservationsAsync(10, CancellationToken.None);
        Assert.Equal("observed", StatusOf(context, payment.PaymentId));

        context.Clock.UtcNow = payment.LateAcceptanceEndsAt.AddHours(6);
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);
        Assert.Equal("observed", StatusOf(context, payment.PaymentId));

        context.Adapter.Observations = [Observation(seenAt, confirmations: 1)];
        await context.Payments.PollBlockchainObservationsAsync(10, CancellationToken.None);

        Assert.Equal("completed", StatusOf(context, payment.PaymentId));
        Assert.DoesNotContain(context.DbContext.PaymentEventHistory, paymentEvent => paymentEvent.EventType == "payment.expired");
    }

    /// <summary>
    /// A double-spent or dropped transaction never confirms. The Payment is
    /// polled for it until the default Confirmation Wait ends, then expires.
    /// </summary>
    [Fact]
    public async Task An_observed_payment_whose_transaction_disappears_expires_when_the_confirmation_wait_ends()
    {
        await using var context = await BuildContextAsync();
        var payment = await CreatePaymentAsync(context);
        var seenAt = payment.ExpiresAt.AddMinutes(-5);
        context.Adapter.Observations = [Observation(seenAt, confirmations: 0)];
        context.Clock.UtcNow = seenAt;
        await context.Payments.PollBlockchainObservationsAsync(10, CancellationToken.None);
        context.Adapter.Observations = [];

        context.Clock.UtcNow = payment.LateAcceptanceEndsAt.AddHours(72).AddSeconds(-1);
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);
        var poll = await context.Payments.PollBlockchainObservationsAsync(10, CancellationToken.None);
        Assert.Equal(1, poll.TargetCount);
        Assert.Equal("observed", StatusOf(context, payment.PaymentId));

        context.Clock.UtcNow = payment.LateAcceptanceEndsAt.AddHours(72);
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);

        Assert.Equal("expired", StatusOf(context, payment.PaymentId));
        Assert.Single(context.DbContext.WebhookOutboxEvents, webhookEvent => webhookEvent.EventType == "payment.expired");
    }

    [Fact]
    public async Task An_observed_payment_expires_when_a_configured_confirmation_wait_ends()
    {
        await using var context = await BuildContextAsync(TimeSpan.FromHours(2));
        var payment = await CreatePaymentAsync(context);
        var seenAt = payment.ExpiresAt.AddMinutes(-5);
        context.Adapter.Observations = [Observation(seenAt, confirmations: 0)];
        context.Clock.UtcNow = seenAt;
        await context.Payments.PollBlockchainObservationsAsync(10, CancellationToken.None);

        context.Clock.UtcNow = payment.LateAcceptanceEndsAt.AddHours(2).AddSeconds(-1);
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);
        Assert.Equal("observed", StatusOf(context, payment.PaymentId));

        context.Clock.UtcNow = payment.LateAcceptanceEndsAt.AddHours(2);
        await context.Payments.ExpireDuePaymentsAsync(10, CancellationToken.None);
        var poll = await context.Payments.PollBlockchainObservationsAsync(10, CancellationToken.None);

        Assert.Equal("expired", StatusOf(context, payment.PaymentId));
        Assert.Equal(0, poll.TargetCount);
    }

    private static BlockchainObservation Observation(DateTimeOffset observedAt, int confirmations) =>
        new("tx-expiry", "0.00039980", observedAt, confirmations, "test-provider", null);

    private static string StatusOf(ExpiryContext context, Guid paymentId)
    {
        context.DbContext.ChangeTracker.Clear();
        return context.DbContext.Payments.Single(payment => payment.Id == paymentId).Status;
    }

    private static async Task<PaymentResponse> CreatePaymentAsync(ExpiryContext context, bool selectCurrency = true)
    {
        context.Clock.UtcNow = CreatedAt;
        var created = await context.Payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand("EUR", 1999, "order-expiry", PaymentContext: null, ReturnUrl: null, "create-order-expiry"),
            CancellationToken.None);
        if (!selectCurrency)
        {
            return created.Payment!;
        }

        var selected = await context.Payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("expiry-payer-page", "btc"),
            CancellationToken.None);
        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selected.Kind);
        return selected.Payment!;
    }

    private async Task<ExpiryContext> BuildContextAsync(TimeSpan? observedConfirmationWait = null)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var clock = new MutableClock();
        var adapter = new SettableBlockchainObservationAdapter();
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddScoped<IBlockchainObservationAdapter>(_ => adapter);
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PayerPageBaseUrl = "https://pay.example.test/pay";
            options.BtcConfirmationRequirement = 1;
            if (observedConfirmationWait is { } wait)
            {
                options.ObservedConfirmationWait = wait;
            }
        });
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("expiry-token"),
            Status = "active",
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt,
        });
        await dbContext.SaveChangesAsync();

        return new ExpiryContext(
            serviceProvider,
            dbContext,
            serviceProvider.GetRequiredService<PaymentApplicationService>(),
            clock,
            adapter);
    }

    private sealed record ExpiryContext(
        ServiceProvider ServiceProvider,
        PayaffeDbContext DbContext,
        PaymentApplicationService Payments,
        MutableClock Clock,
        SettableBlockchainObservationAdapter Adapter) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ServiceProvider.DisposeAsync();
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = CreatedAt;
    }

    private sealed class FixedPayerPageIdGenerator : IPayerPageIdGenerator
    {
        public string Generate() => "expiry-payer-page";
    }

    private sealed class FixedExchangeRateSource : IExchangeRateSource
    {
        public Task<RateLockQuote?> GetRateLockQuoteAsync(
            string fiatCurrency,
            long fiatAmountMinor,
            string supportedCurrency,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult<RateLockQuote?>(new RateLockQuote(
                supportedCurrency,
                "test-rate-source",
                "50000.00",
                "0.00039980",
                requestedAt));
    }

    private sealed class FixedPaymentAddressProvider : IPaymentAddressProvider
    {
        public Task<PaymentAddressAssignment?> AssignAsync(
            Guid projectId,
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken) =>
            Task.FromResult<PaymentAddressAssignment?>(new PaymentAddressAssignment("BTC", PaymentAddress, "mainnet"));
    }

    /// <summary>
    /// Reports whatever the test last put in <see cref="Observations"/> on
    /// every poll.
    /// </summary>
    private sealed class SettableBlockchainObservationAdapter : IBlockchainObservationAdapter
    {
        public IReadOnlyList<BlockchainObservation> Observations { get; set; } = [];

        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult(Observations);
    }
}
