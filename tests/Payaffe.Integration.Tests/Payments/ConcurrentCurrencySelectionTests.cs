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
/// A Payer who double-clicks, or two tabs of the same Payer Page, select a
/// currency at the same time. One selection wins; every other one is told the
/// currency is already selected instead of failing with a server error.
/// </summary>
public sealed class ConcurrentCurrencySelectionTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("9c8b7a6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-04T12:00:00Z");

    [Fact]
    public async Task Concurrent_selections_of_one_payment_yield_one_selection_and_already_selected_answers()
    {
        await using var serviceProvider = await BuildServicesAsync();
        await serviceProvider.GetRequiredService<PaymentApplicationService>().CreateAsync(
            CredentialId,
            new CreatePaymentCommand("EUR", 1999, "order-race", PaymentContext: null, ReturnUrl: null, "create-order-race"),
            CancellationToken.None);

        using var start = new ManualResetEventSlim();
        var selections = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var payments = scope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
            start.Wait();
            return await payments.SelectCurrencyAsync(
                new SelectPaymentCurrencyCommand("race-payer-page", "btc"),
                CancellationToken.None);
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(selections);

        Assert.Single(results, result => result.Kind == SelectPaymentCurrencyResultKind.Selected);
        Assert.All(
            results.Where(result => result.Kind != SelectPaymentCurrencyResultKind.Selected),
            result => Assert.Equal(SelectPaymentCurrencyResultKind.AlreadySelected, result.Kind));
        await using var verification = serviceProvider.CreateAsyncScope();
        var dbContext = verification.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Single(dbContext.RateLocks);
        Assert.Single(dbContext.PaymentEventHistory, paymentEvent => paymentEvent.EventType == "payment.currency_selected");
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
        services.Configure<PaymentApplicationOptions>(options => options.PayerPageBaseUrl = "https://pay.example.test/pay");
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("race-token"),
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
        public string Generate() => "race-payer-page";
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
            Task.FromResult<PaymentAddressAssignment?>(
                new PaymentAddressAssignment("BTC", "bc1qpayafferaceaddress0000000000000000000000", "mainnet"));
    }
}
