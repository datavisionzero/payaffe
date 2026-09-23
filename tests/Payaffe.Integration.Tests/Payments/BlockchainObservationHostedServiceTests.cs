using System.Collections.Concurrent;
using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// A transaction is routinely first seen in the mempool, short of its
    /// Confirmation Requirement. The poll that later reports it deep enough is
    /// what completes the Payment; it is the same transaction, so it is not
    /// recorded twice.
    /// </summary>
    [Fact]
    public async Task A_transaction_first_seen_without_confirmations_completes_the_payment_when_a_later_poll_confirms_it()
    {
        var observationAdapter = new QueuedBlockchainObservationAdapter(
            new BlockchainObservation("tx-mempool", "0.00039980", WorkerNow.AddMinutes(1), Confirmations: 0, "test-provider", null),
            new BlockchainObservation("tx-mempool", "0.00039980", WorkerNow.AddMinutes(1), Confirmations: 1, "test-provider", null));
        await using var context = await BuildContextAsync(observationAdapter);
        var payments = context.ServiceProvider.GetRequiredService<PaymentApplicationService>();

        var first = await payments.PollBlockchainObservationsAsync(10, CancellationToken.None);
        context.DbContext.ChangeTracker.Clear();
        Assert.Equal("observed", Assert.Single(context.DbContext.Payments).Status);

        var second = await payments.PollBlockchainObservationsAsync(10, CancellationToken.None);
        context.DbContext.ChangeTracker.Clear();

        Assert.Equal(0, first.CompletedCount);
        Assert.Equal(1, second.AlreadyRecordedCount);
        Assert.Equal(1, second.CompletedCount);
        Assert.Equal(0, second.FailedCount);
        Assert.Equal("completed", Assert.Single(context.DbContext.Payments).Status);
        Assert.Equal(1, Assert.Single(context.DbContext.MatchingBlockchainTransactions).Confirmations);
        Assert.Single(context.DbContext.WebhookOutboxEvents, webhookEvent => webhookEvent.EventType == "payment.completed");
    }

    /// <summary>
    /// One failed write must stay with its Payment. The whole run shares one
    /// database context, so a write that fails and is not discarded would be
    /// sent again by every later write of the run, including the lease
    /// release, and the same Payment would fail first on every tick.
    /// </summary>
    [Fact]
    public async Task A_failed_write_for_one_payment_does_not_fail_the_rest_of_the_run()
    {
        Guid? poisonedPaymentId = null;
        var observationAdapter = new DelegatingBlockchainObservationAdapter(target =>
        {
            poisonedPaymentId ??= target.PaymentId;
            var transactionHash = target.PaymentId == poisonedPaymentId ? "tx-poison" : $"tx-{target.PaymentId:N}";
            return [new BlockchainObservation(transactionHash, "0.00039980", WorkerNow.AddMinutes(1), 1, "test-provider", null)];
        });
        var logs = new CapturingLoggerProvider();
        await using var context = await BuildContextAsync(observationAdapter, paymentCount: 2, logs);
        await context.DbContext.Database.ExecuteSqlRawAsync(
            """
            create function app.reject_poisoned_transaction() returns trigger language plpgsql as $$
            begin
                if new.transaction_hash = 'tx-poison' then
                    raise exception 'poisoned write';
                end if;
                return new;
            end $$;
            create trigger reject_poisoned_transaction before insert on app.matching_blockchain_transactions
                for each row execute function app.reject_poisoned_transaction();
            """);
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
            await WaitUntilAsync(context.DbContext, dbContext => dbContext.BackgroundWorkerLeases.Any(
                lease => lease.WorkerName == "blockchain-observation" && lease.LastSucceededAt != null));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        context.DbContext.ChangeTracker.Clear();
        var payments = context.DbContext.Payments.ToList();
        Assert.Equal("waiting_for_payment", Assert.Single(payments, payment => payment.Id == poisonedPaymentId).Status);
        Assert.Equal("completed", Assert.Single(payments, payment => payment.Id != poisonedPaymentId).Status);
        var lease = Assert.Single(context.DbContext.BackgroundWorkerLeases, lease => lease.WorkerName == "blockchain-observation");
        Assert.Equal(0, lease.ConsecutiveFailureCount);
        Assert.Contains(
            logs.Entries,
            entry => entry.Level == LogLevel.Error &&
                     entry.Exception is not null &&
                     entry.Message.Contains(poisonedPaymentId!.Value.ToString(), StringComparison.Ordinal));
    }

    private async Task<WorkerContext> BuildContextAsync(
        IBlockchainObservationAdapter observationAdapter,
        int paymentCount = 1,
        CapturingLoggerProvider? logs = null)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        if (logs is not null)
        {
            services.AddLogging(builder => builder.AddProvider(logs));
        }

        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, SequentialPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddScoped(_ => observationAdapter);
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
        for (var number = 1; number <= paymentCount; number++)
        {
            await payments.CreateAsync(
                CredentialId,
                new CreatePaymentCommand(
                    "EUR",
                    1999,
                    $"order-{number}",
                    PaymentContext: null,
                    ReturnUrl: null,
                    $"create-order-{number}"),
                CancellationToken.None);
            var selectResult = await payments.SelectCurrencyAsync(
                new SelectPaymentCurrencyCommand($"payer-page-{number}", "btc"),
                CancellationToken.None);
            Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selectResult.Kind);
        }

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
        services.AddSingleton<IPayerPageIdGenerator, SequentialPayerPageIdGenerator>();
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
            new SelectPaymentCurrencyCommand("payer-page-1", "btc"),
            CancellationToken.None);
        await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
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

    private static async Task WaitUntilAsync(PayaffeDbContext dbContext, Func<PayaffeDbContext, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            dbContext.ChangeTracker.Clear();
            if (condition(dbContext))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        dbContext.ChangeTracker.Clear();
        Assert.True(condition(dbContext), "The worker did not reach the expected state in time.");
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

    private sealed class SequentialPayerPageIdGenerator : IPayerPageIdGenerator
    {
        private int _next;

        public string Generate() => $"payer-page-{Interlocked.Increment(ref _next)}";
    }

    /// <summary>
    /// Answers every poll from a function of its target and remembers which
    /// targets were polled, in order.
    /// </summary>
    private sealed class DelegatingBlockchainObservationAdapter(
        Func<BlockchainObservationTarget, IReadOnlyList<BlockchainObservation>> poll)
        : IBlockchainObservationAdapter
    {
        public ConcurrentQueue<BlockchainObservationTarget> PolledTargets { get; } = new();

        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            PolledTargets.Enqueue(target);
            return Task.FromResult(poll(target));
        }
    }

    private sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<CapturedLogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
        }
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
                "BTC",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "mainnet"));
        }
    }

    /// <summary>
    /// Reports one queued observation per poll, in order, and nothing once
    /// the queue is empty.
    /// </summary>
    private sealed class QueuedBlockchainObservationAdapter(params BlockchainObservation[] observations)
        : IBlockchainObservationAdapter
    {
        private readonly Queue<BlockchainObservation> _observations = new(observations);

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

            return Task.FromResult<IReadOnlyList<BlockchainObservation>>(
                _observations.TryDequeue(out var observation) ? [observation] : []);
        }
    }
}
