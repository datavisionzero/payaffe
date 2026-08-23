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

public sealed class PaymentLifecycleHostedServiceTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("0acb70c4-79dd-4de9-88c8-ac6ec0b52949");
    private static readonly DateTimeOffset WorkerNow = DateTimeOffset.Parse("2026-07-04T12:00:00Z");

    [Fact]
    public async Task HostedService_expires_due_payments_when_enabled()
    {
        await using var context = await BuildContextAsync();
        using var worker = new PaymentLifecycleHostedService(
            context.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentLifecycleWorkerOptions
            {
                Enabled = true,
                PollInterval = TimeSpan.FromHours(1),
                ExpirationBatchSize = 10,
            }),
            NullLogger<PaymentLifecycleHostedService>.Instance,
            SchemaMigrationState.AlreadyApplied());

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitForPaymentStatusAsync(context.DbContext, "expired");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        context.DbContext.ChangeTracker.Clear();
        var payment = Assert.Single(context.DbContext.Payments);
        Assert.Equal("expired", payment.Status);
        Assert.Contains(
            context.DbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.expired");
        Assert.Contains(
            context.DbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.expired");
    }

    private async Task<WorkerContext> BuildContextAsync()
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
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PayerPageBaseUrl = "https://pay.example.test/pay";
            options.PaymentExpiration = TimeSpan.FromMinutes(30);
            options.LateAcceptanceWindow = TimeSpan.FromMinutes(30);
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

        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(dbContext.Payments);
        payment.LateAcceptanceEndsAt = WorkerNow.AddMinutes(-1);
        await dbContext.SaveChangesAsync();

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
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentAddressAssignment?>(new PaymentAddressAssignment(
                supportedCurrency,
                $"{supportedCurrency.ToLowerInvariant()}-test-address"));
        }
    }
}
