using System.Collections.Concurrent;
using Payaffe.Application;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Payaffe.Integration.Tests.Payments;

/// <summary>
/// A Payment Address can carry history, for example after the derivation
/// cursor restarted on a restored database. A transaction observed more than
/// the tolerance before currency selection does not count toward the Payment
/// and raises an Address History Alert (ADR 0035).
/// </summary>
public sealed class PreSelectionHistoryTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("4d7e1f2a-8b3c-4e5d-9f60-7a8b9c0d1e2f");
    private static readonly DateTimeOffset SelectedAt = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
    private const string PaymentAddress = "bc1qpayaffehistoryaddress00000000000000000000";

    [Fact]
    public async Task A_transaction_the_address_received_before_selection_does_not_count_and_raises_one_alert()
    {
        var observedAt = SelectedAt.AddHours(-3);
        var logs = new ConcurrentQueue<(LogLevel Level, string Message)>();
        await using var serviceProvider = await BuildServicesAsync(observedAt, logs);
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var paymentId = await CreateSelectedPaymentAsync(payments);

        var first = await payments.PollBlockchainObservationsAsync(10, CancellationToken.None);
        var second = await payments.PollBlockchainObservationsAsync(10, CancellationToken.None);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.ChangeTracker.Clear();
        Assert.Equal("waiting_for_payment", dbContext.Payments.Single(payment => payment.Id == paymentId).Status);
        Assert.Empty(dbContext.MatchingBlockchainTransactions);
        Assert.Equal(1, first.RejectedCount);
        Assert.Equal(1, second.RejectedCount);
        var alerts = await serviceProvider.GetRequiredService<AdminPaymentQueryService>()
            .ListAddressHistoryAlertsAsync(ProjectDefaults.DefaultProjectId, requestedLimit: null, CancellationToken.None);
        var alert = Assert.Single(alerts);
        Assert.Equal(paymentId, alert.PaymentId);
        Assert.Equal("tx-history", alert.TransactionHash);
        Assert.Equal(observedAt, alert.ObservedAt);
        Assert.Equal(SelectedAt, alert.CurrencySelectedAt);
        Assert.Equal("open", alert.Status);
        Assert.Single(logs, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("tx-history", StringComparison.Ordinal));
    }

    /// <summary>
    /// Block timestamps can trail real time, so a transaction stamped shortly
    /// before selection still counts.
    /// </summary>
    [Fact]
    public async Task A_transaction_stamped_within_the_tolerance_before_selection_still_counts()
    {
        await using var serviceProvider = await BuildServicesAsync(SelectedAt.AddHours(-1), logs: null);
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var paymentId = await CreateSelectedPaymentAsync(payments);

        await payments.PollBlockchainObservationsAsync(10, CancellationToken.None);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.ChangeTracker.Clear();
        Assert.Equal("completed", dbContext.Payments.Single(payment => payment.Id == paymentId).Status);
        Assert.Empty(dbContext.AddressHistoryAlerts);
    }

    private static async Task<Guid> CreateSelectedPaymentAsync(PaymentApplicationService payments)
    {
        var created = await payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand("EUR", 1999, "order-history", PaymentContext: null, ReturnUrl: null, "create-order-history"),
            CancellationToken.None);
        var selected = await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("history-payer-page", "btc"),
            CancellationToken.None);
        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selected.Kind);
        return created.Payment!.PaymentId;
    }

    private async Task<ServiceProvider> BuildServicesAsync(
        DateTimeOffset observedAt,
        ConcurrentQueue<(LogLevel Level, string Message)>? logs)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        if (logs is not null)
        {
            services.AddLogging(builder => builder.AddProvider(new CapturingLoggerProvider(logs)));
        }

        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddScoped<IBlockchainObservationAdapter>(_ => new HistoryAdapter(observedAt));
        services.Configure<PaymentApplicationOptions>(options => options.PayerPageBaseUrl = "https://pay.example.test/pay");
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("history-token"),
            Status = "active",
            CreatedAt = SelectedAt,
            UpdatedAt = SelectedAt,
        });
        await dbContext.SaveChangesAsync();
        return serviceProvider;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => SelectedAt;
    }

    private sealed class FixedPayerPageIdGenerator : IPayerPageIdGenerator
    {
        public string Generate() => "history-payer-page";
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

    /// <summary>A confirmed transaction paying the expected amount at a fixed time.</summary>
    private sealed class HistoryAdapter(DateTimeOffset observedAt) : IBlockchainObservationAdapter
    {
        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlockchainObservation>>(
                [new BlockchainObservation("tx-history", target.ExpectedCryptoAmount, observedAt, 6, "test-provider", null)]);
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
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
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
