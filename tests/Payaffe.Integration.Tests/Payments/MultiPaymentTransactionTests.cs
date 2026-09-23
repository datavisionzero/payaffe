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
/// A batched withdrawal pays several Payment Addresses in one Blockchain
/// Transaction. It is a Matching Blockchain Transaction of every Payment it
/// pays, whether those Payments share a Project or not.
/// </summary>
public sealed class MultiPaymentTransactionTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid FirstCredentialId = Guid.Parse("0f0c8f0e-5c3f-4f0a-9d8e-4b5a7c1d2e3f");
    private static readonly Guid SecondCredentialId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid SecondProjectId = Guid.Parse("7e8f9a0b-1c2d-4e3f-8a4b-5c6d7e8f9a0b");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-04T12:00:00Z");

    [Fact]
    public async Task One_transaction_paying_payments_in_one_and_in_two_projects_completes_each_of_them()
    {
        await using var serviceProvider = await BuildServicesAsync();
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var sameProjectFirst = await CreateSelectedPaymentAsync(payments, FirstCredentialId, "order-1");
        var sameProjectSecond = await CreateSelectedPaymentAsync(payments, FirstCredentialId, "order-2");
        var otherProject = await CreateSelectedPaymentAsync(payments, SecondCredentialId, "order-3");

        var result = await payments.PollBlockchainObservationsAsync(10, CancellationToken.None);

        Assert.Equal(0, result.FailedCount);
        Assert.Equal(3, result.CompletedCount);
        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.ChangeTracker.Clear();
        foreach (var paymentId in new[] { sameProjectFirst, sameProjectSecond, otherProject })
        {
            Assert.Equal("completed", dbContext.Payments.Single(payment => payment.Id == paymentId).Status);
            Assert.Single(
                dbContext.MatchingBlockchainTransactions,
                transaction => transaction.PaymentId == paymentId && transaction.TransactionHash == "tx-batched-withdrawal");
        }

        Assert.Equal(
            SecondProjectId,
            dbContext.MatchingBlockchainTransactions.Single(transaction => transaction.PaymentId == otherProject).ProjectId);
    }

    private static async Task<Guid> CreateSelectedPaymentAsync(
        PaymentApplicationService payments,
        Guid credentialId,
        string externalReference)
    {
        var created = await payments.CreateAsync(
            credentialId,
            new CreatePaymentCommand("EUR", 1999, externalReference, PaymentContext: null, ReturnUrl: null, $"create-{externalReference}"),
            CancellationToken.None);
        var selected = await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand(new Uri(created.Payment!.PayerPageUrl).Segments[^1], "btc"),
            CancellationToken.None);
        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selected.Kind);
        return created.Payment.PaymentId;
    }

    private async Task<ServiceProvider> BuildServicesAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, SequentialPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, PerPaymentAddressProvider>();
        services.AddScoped<IBlockchainObservationAdapter, BatchedWithdrawalAdapter>();
        services.Configure<PaymentApplicationOptions>(options => options.PayerPageBaseUrl = "https://pay.example.test/pay");
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.Projects.Add(new ProjectRecord
        {
            Id = SecondProjectId,
            Name = "Second Project",
            Slug = "second-project",
            Status = "active",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        dbContext.ProjectConfigurations.Add(new ProjectConfigurationRecord
        {
            ProjectId = SecondProjectId,
            PaymentExpirationSeconds = 3600,
            LateAcceptanceWindowSeconds = 86400,
            PaymentTolerancePercent = 1m,
            BtcEnabled = true,
            LtcEnabled = true,
            EthEnabled = true,
            BtcConfirmationRequirement = 1,
            LtcConfirmationRequirement = 1,
            EthConfirmationRequirement = 12,
            BtcReorgMonitoringDepth = 6,
            LtcReorgMonitoringDepth = 12,
            EthReorgMonitoringDepth = 64,
            NativeEthLowCapacityThreshold = 20,
            LegacySettingsFingerprint = string.Empty,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        foreach (var (credentialId, projectId, token) in new[]
                 {
                     (FirstCredentialId, ProjectDefaults.DefaultProjectId, "first-token"),
                     (SecondCredentialId, SecondProjectId, "second-token"),
                 })
        {
            dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
            {
                ProjectId = projectId,
                Id = credentialId,
                Name = "Test credential",
                TokenHash = IntegrationApiCredentialTokenHasher.HashToken(token),
                Status = "active",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
        }

        await dbContext.SaveChangesAsync();
        return serviceProvider;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class SequentialPayerPageIdGenerator : IPayerPageIdGenerator
    {
        private int _next;

        public string Generate() => $"batched-payer-page-{Interlocked.Increment(ref _next)}";
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

    private sealed class PerPaymentAddressProvider : IPaymentAddressProvider
    {
        public Task<PaymentAddressAssignment?> AssignAsync(
            Guid projectId,
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken) =>
            Task.FromResult<PaymentAddressAssignment?>(
                new PaymentAddressAssignment("BTC", $"bc1q{paymentId:N}", "mainnet"));
    }

    /// <summary>
    /// Every Payment Address was paid its expected amount by the same
    /// confirmed transaction.
    /// </summary>
    private sealed class BatchedWithdrawalAdapter : IBlockchainObservationAdapter
    {
        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlockchainObservation>>(
            [
                new BlockchainObservation(
                    "tx-batched-withdrawal",
                    target.ExpectedCryptoAmount,
                    Now.AddMinutes(1),
                    Confirmations: 1,
                    "test-provider",
                    ProviderObservationId: null),
            ]);
    }
}
