using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Persistence;

/// <summary>
/// Every payment write looks the Payment up inside the Project it was given.
/// A Payment of one Project is not found through another Project, and an
/// empty Project finds nothing rather than everything.
/// </summary>
public sealed class PaymentProjectScopingTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("c3b5a4f0-2f7e-4b8e-9f51-7a0d7f7b61c4");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
    private const string PaymentAddress = "bc1qpayaffescopingaddress000000000000000000000";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Payment_writes_do_not_find_a_payment_through_another_or_an_empty_project(bool emptyProject)
    {
        await using var serviceProvider = await BuildServicesAsync();
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var created = await payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand("EUR", 1999, "order-scoping", PaymentContext: null, ReturnUrl: null, "create-order-scoping"),
            CancellationToken.None);
        var paymentId = created.Payment!.PaymentId;
        var otherProjectId = emptyProject ? Guid.Empty : Guid.NewGuid();
        var store = serviceProvider.GetRequiredService<IPaymentStore>();

        var selection = await store.SelectCurrencyAsync(
            new PaymentSelectionDraft(
                paymentId, "BTC", "0.00039980", PaymentAddress, "mainnet", ChainId: null,
                "test-rate-source", "50000.00", Now, 1, 1m, 6, Now, otherProjectId),
            new PaymentEventDraft(Guid.NewGuid(), paymentId, "payment.currency_selected", Now, Details: null),
            WebhookEvent(paymentId, "payment.currency_selected"),
            CancellationToken.None);
        Assert.Equal(SelectCurrencyStoreResultKind.NotFound, selection.Kind);

        var selected = await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("scoping-payer-page", "btc"),
            CancellationToken.None);
        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selected.Kind);

        var observation = await store.RecordBlockchainObservationAsync(
            new BlockchainObservationDraft(
                Guid.NewGuid(), paymentId, "BTC", PaymentAddress, "tx-scoping", "0.00039980",
                Now, Now, 1, "test-provider", null, Now, Now, otherProjectId),
            new PaymentCompletionPolicyDraft(1, 1m),
            new PaymentEventDraft(Guid.NewGuid(), paymentId, "payment.observed", Now, Details: null),
            WebhookEvent(paymentId, "payment.observed"),
            new PaymentEventDraft(Guid.NewGuid(), paymentId, "payment.completed", Now, Details: null),
            WebhookEvent(paymentId, "payment.completed"),
            CancellationToken.None);
        Assert.Equal(RecordBlockchainObservationStoreResultKind.PaymentNotFound, observation.Kind);

        var confirmation = await store.UpdateBlockchainTransactionConfirmationsAsync(
            new BlockchainTransactionConfirmationUpdateDraft(
                paymentId, "BTC", "tx-scoping", 1, BlockHash: null, BlockHeight: null, Now, otherProjectId),
            new PaymentCompletionPolicyDraft(1, 1m),
            new PaymentEventDraft(Guid.NewGuid(), paymentId, "payment.completed", Now, Details: null),
            WebhookEvent(paymentId, "payment.completed"),
            new PaymentEventDraft(Guid.NewGuid(), paymentId, "payment.reorg_alerted", Now, Details: null),
            CancellationToken.None);
        Assert.Equal(UpdateBlockchainTransactionConfirmationsStoreResultKind.PaymentNotFound, confirmation.Kind);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.ChangeTracker.Clear();
        Assert.Equal("waiting_for_payment", dbContext.Payments.Single(payment => payment.Id == paymentId).Status);
        Assert.Empty(dbContext.MatchingBlockchainTransactions);
    }

    private static WebhookOutboxEventDraft WebhookEvent(Guid paymentId, string eventType)
    {
        var eventId = Guid.NewGuid();
        return new WebhookOutboxEventDraft(
            eventId, paymentId, CredentialId, eventType, "1", 1, "payment", paymentId.ToString("D"),
            "pending", Now, Now, Now, 0, eventId.ToString("D"));
    }

    private async Task<ServiceProvider> BuildServicesAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddScoped<IBlockchainObservationAdapter, NoOpBlockchainObservationAdapter>();
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("scoping-token"),
            Status = "active",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await dbContext.SaveChangesAsync();
        return serviceProvider;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FixedPayerPageIdGenerator : IPayerPageIdGenerator
    {
        public string Generate() => "scoping-payer-page";
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
}
