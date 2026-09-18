using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Payments;

public sealed class BlockchainObservationHostedServiceTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("7d14dfb1-4c17-47d7-9a7c-048992a93a4f");
    private static readonly DateTimeOffset WorkerNow = DateTimeOffset.Parse("2026-07-04T12:00:00Z");

    [Fact]
    public async Task HostedService_records_polled_blockchain_observations_when_enabled()
    {
        var observationAdapter = new QueuedBlockchainObservationAdapter(new BlockchainObservation(
            "tx-123",
            "0.00039980",
            WorkerNow.AddMinutes(1),
            Confirmations: 1,
            "test-provider",
            "provider-observation-123"));
        await using var context = await BuildContextAsync(observationAdapter);
        using var worker = new BlockchainObservationHostedService(
            context.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BlockchainObservationWorkerOptions
            {
                Enabled = true,
                PollInterval = TimeSpan.FromHours(1),
                MaxPaymentsPerPoll = 10,
            }),
            NullLogger<BlockchainObservationHostedService>.Instance,
            SchemaMigrationState.AlreadyApplied());

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForPaymentStatusAsync(context.DbContext, "completed");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        context.DbContext.ChangeTracker.Clear();
        var payment = Assert.Single(context.DbContext.Payments);
        Assert.Equal("completed", payment.Status);
        Assert.Equal(1, observationAdapter.PollCount);
        Assert.NotNull(observationAdapter.Target);
        Assert.Equal(payment.Id, observationAdapter.Target.PaymentId);
        Assert.Contains(
            context.DbContext.MatchingBlockchainTransactions,
            transaction => transaction.TransactionHash == "tx-123" &&
                           transaction.ProviderName == "test-provider" &&
                           transaction.Confirmations == 1);
        Assert.Contains(
            context.DbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.observed");
        Assert.Contains(
            context.DbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.completed");
        Assert.Contains(
            context.DbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.observed");
        Assert.Contains(
            context.DbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.completed");
    }

    [Fact]
    public async Task ReorgMonitoringHostedService_creates_reorg_alert_for_completed_payment_confirmation_drop()
    {
        var observationAdapter = new QueuedBlockchainObservationAdapter(new BlockchainObservation(
            "tx-reorg-123",
            "0.00039980",
            WorkerNow.AddMinutes(1),
            Confirmations: 0,
            "test-provider",
            "provider-observation-123"));
        await using var context = await BuildReorgMonitoringContextAsync(observationAdapter);
        using var worker = new ReorgMonitoringHostedService(
            context.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReorgMonitoringWorkerOptions
            {
                Enabled = true,
                PollInterval = TimeSpan.FromHours(1),
                MaxTransactionsPerPoll = 10,
            }),
            NullLogger<ReorgMonitoringHostedService>.Instance,
            SchemaMigrationState.AlreadyApplied());

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForReorgAlertAsync(context.DbContext);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        context.DbContext.ChangeTracker.Clear();
        var payment = Assert.Single(context.DbContext.Payments);
        Assert.Equal("completed", payment.Status);
        Assert.Equal(1, observationAdapter.PollCount);
        Assert.NotNull(observationAdapter.Target);
        Assert.Equal(payment.Id, observationAdapter.Target.PaymentId);

        var matchingTransaction = Assert.Single(context.DbContext.MatchingBlockchainTransactions);
        Assert.True(matchingTransaction.ReorgAffected);
        Assert.Equal(0, matchingTransaction.Confirmations);

        var reorgAlert = Assert.Single(context.DbContext.ReorgAlerts);
        Assert.Equal(payment.Id, reorgAlert.PaymentId);
        Assert.Equal(matchingTransaction.Id, reorgAlert.MatchingBlockchainTransactionId);
        Assert.Equal("tx-reorg-123", reorgAlert.TransactionHash);
        Assert.Equal(1, reorgAlert.PreviousConfirmations);
        Assert.Equal(0, reorgAlert.NewConfirmations);
        Assert.Equal("open", reorgAlert.Status);
        Assert.Contains(
            context.DbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.reorg_alerted");
    }

    private async Task<WorkerContext> BuildContextAsync(QueuedBlockchainObservationAdapter observationAdapter)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddSingleton(observationAdapter);
        services.AddScoped<IBlockchainObservationAdapter>(provider =>
            provider.GetRequiredService<QueuedBlockchainObservationAdapter>());
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PayerPageBaseUrl = "https://pay.example.test/pay";
            options.PaymentExpiration = TimeSpan.FromMinutes(30);
            options.LateAcceptanceWindow = TimeSpan.FromMinutes(30);
            options.BtcConfirmationRequirement = 1;
        });
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);
        await SeedCredentialAsync(serviceProvider);

        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        await payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        var selectResult = await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);
        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selectResult.Kind);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        return new WorkerContext(serviceProvider, dbContext);
    }

    private async Task<WorkerContext> BuildReorgMonitoringContextAsync(QueuedBlockchainObservationAdapter observationAdapter)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddSingleton(observationAdapter);
        services.AddScoped<IBlockchainObservationAdapter>(provider =>
            provider.GetRequiredService<QueuedBlockchainObservationAdapter>());
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PayerPageBaseUrl = "https://pay.example.test/pay";
            options.PaymentExpiration = TimeSpan.FromMinutes(30);
            options.LateAcceptanceWindow = TimeSpan.FromMinutes(30);
            options.BtcConfirmationRequirement = 1;
            options.BtcReorgMonitoringDepth = 6;
        });
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);
        await SeedCredentialAsync(serviceProvider);

        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);
        await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "btc-test-address",
                "tx-reorg-123",
                "0.00039980",
                WorkerNow.AddMinutes(1),
                Confirmations: 0,
                "test-provider",
                "provider-observation-setup"),
            CancellationToken.None);
        var completionResult = await payments.UpdateBlockchainTransactionConfirmationsAsync(
            new UpdateBlockchainTransactionConfirmationsCommand(
                createResult.Payment.PaymentId,
                "btc",
                "tx-reorg-123",
                Confirmations: 1,
                BlockHash: "block-original",
                BlockHeight: 840000),
            CancellationToken.None);
        Assert.Equal(UpdateBlockchainTransactionConfirmationsResultKind.Completed, completionResult.Kind);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        return new WorkerContext(serviceProvider, dbContext);
    }

    private static async Task SeedCredentialAsync(IServiceProvider serviceProvider)
    {
        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("valid-token"),
            Status = "active",
            CreatedAt = WorkerNow,
            UpdatedAt = WorkerNow,
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task WaitForPaymentStatusAsync(
        PayaffeDbContext dbContext,
        string expectedStatus)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            dbContext.ChangeTracker.Clear();
            var payment = Assert.Single(dbContext.Payments);
            if (payment.Status == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        dbContext.ChangeTracker.Clear();
        var finalPayment = Assert.Single(dbContext.Payments);
        Assert.Equal(expectedStatus, finalPayment.Status);
    }

    private static async Task WaitForReorgAlertAsync(PayaffeDbContext dbContext)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            dbContext.ChangeTracker.Clear();
            if (dbContext.ReorgAlerts.Any())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        dbContext.ChangeTracker.Clear();
        Assert.NotEmpty(dbContext.ReorgAlerts);
    }

    private sealed record WorkerContext(
        ServiceProvider ServiceProvider,
        PayaffeDbContext DbContext) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ServiceProvider.DisposeAsync();
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => WorkerNow;
    }

    private sealed class FixedPayerPageIdGenerator : IPayerPageIdGenerator
    {
        public string Generate() => "fixed-payer-page-id";
    }

    private sealed class FixedExchangeRateSource : IExchangeRateSource
    {
        public Task<RateLockQuote?> GetRateLockQuoteAsync(
            string fiatCurrency,
            long fiatAmountMinor,
            string supportedCurrency,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<RateLockQuote?>(new RateLockQuote(
                supportedCurrency,
                "test-rate-source",
                "50000.00",
                "0.00039980",
                requestedAt));
        }
    }

    private sealed class FixedPaymentAddressProvider : IPaymentAddressProvider
    {
        public Task<PaymentAddressAssignment?> AssignAsync(
            Guid projectId,
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentAddressAssignment?>(new PaymentAddressAssignment(
                supportedCurrency,
                $"{supportedCurrency.ToLowerInvariant()}-test-address"));
        }
    }

    private sealed class QueuedBlockchainObservationAdapter(BlockchainObservation observation)
        : IBlockchainObservationAdapter
    {
        private bool _returnedObservation;

        public BlockchainObservationTarget? Target { get; private set; }

        public int PollCount { get; private set; }

        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            Target = target;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            Target = target;
            PollCount++;

            if (_returnedObservation)
            {
                return Task.FromResult<IReadOnlyList<BlockchainObservation>>([]);
            }

            _returnedObservation = true;
            return Task.FromResult<IReadOnlyList<BlockchainObservation>>([observation]);
        }
    }
}
