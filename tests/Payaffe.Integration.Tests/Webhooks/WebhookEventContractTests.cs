using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Payaffe.Application;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Webhooks;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Webhooks;

/// <summary>
/// Generates and compares one accepted payload snapshot per Payment Webhook
/// Event type. Every payload is produced by driving the real lifecycle and then
/// delivering the resulting Webhook Outbox event, so a snapshot can only change
/// when the delivered contract changes.
/// </summary>
public sealed class WebhookEventContractTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string SnapshotDirectoryFromRepositoryRoot = "docs/contracts/webhooks";
    private const string NormalizedUuid = "00000000-0000-0000-0000-000000000000";
    private const string NormalizedPayerPageId = "fixed-payer-page-id";

    private static readonly Guid CredentialId = Guid.Parse("78d8a09b-1c4a-4b6e-9f94-f8acbd4278f1");
    private static readonly Guid AdminAccountId = Guid.Parse("6b1d1d1c-9b1e-4c5a-8f2d-6f9b5f0a1c33");
    private static readonly DateTimeOffset LifecycleStart = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
    private static readonly DateTimeOffset ObservationTime = DateTimeOffset.Parse("2026-07-04T12:05:00Z");

    /// <summary>Past the one-hour expiration plus the 24-hour Late Acceptance Window.</summary>
    private static readonly DateTimeOffset AfterLateAcceptance = DateTimeOffset.Parse("2026-07-05T13:30:00Z");

    private static readonly Regex UuidRegex = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    /// <summary>
    /// The event types an Admin can subscribe a Webhook Endpoint to. Every one
    /// of them needs an accepted snapshot, so the catalog is asserted here as
    /// well as covered file by file.
    /// </summary>
    private static readonly string[] ExpectedEventTypes =
    [
        "payment.created",
        "payment.currency_selected",
        "payment.observed",
        "payment.completed",
        "payment.expired",
        "payment.settled",
    ];

    [Fact]
    public async Task Every_Payment_event_payload_matches_its_accepted_contract_snapshot()
    {
        var payloads = await DeliverWholeLifecycleAsync();

        Assert.Equal(ExpectedEventTypes.Order(StringComparer.Ordinal), payloads.Keys.Order(StringComparer.Ordinal));

        var missingSnapshots = new List<string>();
        var drifted = new List<string>();
        foreach (var eventType in ExpectedEventTypes)
        {
            var snapshotPath = Path.Combine(
                FindRepositoryRoot(),
                SnapshotDirectoryFromRepositoryRoot,
                $"{SnapshotFileName(eventType)}");
            var generated = payloads[eventType];
            if (ShouldUpdateSnapshot())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
                await File.WriteAllTextAsync(snapshotPath, generated);
            }

            if (!File.Exists(snapshotPath))
            {
                missingSnapshots.Add(snapshotPath);
                continue;
            }

            var accepted = NormalizeJson(await File.ReadAllTextAsync(snapshotPath));
            if (!string.Equals(accepted, generated, StringComparison.Ordinal))
            {
                drifted.Add($"{eventType}:{Environment.NewLine}{accepted}{Environment.NewLine}--- generated ---{Environment.NewLine}{generated}");
            }
        }

        Assert.True(
            missingSnapshots.Count == 0,
            $"Accepted Webhook payload snapshots are missing: {string.Join(", ", missingSnapshots)}.");
        Assert.True(
            drifted.Count == 0,
            $"Delivered Webhook payloads drifted from their accepted snapshots:{Environment.NewLine}{string.Join(Environment.NewLine, drifted)}");
    }

    [Fact]
    public async Task Every_delivered_event_carries_the_event_type_header_of_its_payload()
    {
        var payloads = await DeliverWholeLifecycleAsync();

        foreach (var eventType in ExpectedEventTypes)
        {
            using var document = JsonDocument.Parse(payloads[eventType]);
            Assert.Equal(eventType, document.RootElement.GetProperty("event_type").GetString());
            Assert.Equal("1", document.RootElement.GetProperty("event_version").GetString());
            Assert.Equal("payment", document.RootElement.GetProperty("resource").GetProperty("type").GetString());
        }
    }

    /// <summary>
    /// Drives two Payments so that all six event types occur: one completes
    /// after a confirmed observation, the other is observed without
    /// confirmations, expires, and is then settled by an Admin.
    /// </summary>
    /// <remarks>
    /// The Webhook payload carries the Payment state as of delivery, not as of
    /// the event, so the outbox is drained after every lifecycle step. That is
    /// also how the delivery worker behaves in a running installation.
    /// </remarks>
    private async Task<Dictionary<string, string>> DeliverWholeLifecycleAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var handler = new CapturingHttpMessageHandler();
        var clock = new MutableClock(LifecycleStart);
        await using var serviceProvider = BuildServiceProvider(connectionString, handler, clock);
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);
        await SeedAsync(serviceProvider);

        var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();

        var completing = await payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand("EUR", 1999, "order-123", PaymentContext: null, ReturnUrl: null, "create-completing"),
            CancellationToken.None);
        var expiring = await payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand("EUR", 1999, "order-123", PaymentContext: null, ReturnUrl: null, "create-expiring"),
            CancellationToken.None);
        await DrainAsync(serviceProvider, handler, payloads);

        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand(PayerPageIdOf(completing), "btc"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand(PayerPageIdOf(expiring), "btc"),
            CancellationToken.None);
        await DrainAsync(serviceProvider, handler, payloads);

        // An unconfirmed observation leaves the Payment observed, which is both
        // the `payment.observed` contract example and the Matching Blockchain
        // Transaction that later makes manual Settlement eligible.
        var observed = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                expiring.Payment!.PaymentId,
                "btc",
                "btc-test-address",
                "tx-unconfirmed-456",
                "0.00039980",
                ObservationTime,
                Confirmations: 0,
                "test-provider",
                "provider-observation-456"),
            CancellationToken.None);
        Assert.Equal(RecordBlockchainObservationResultKind.Observed, observed.Kind);
        await DrainAsync(serviceProvider, handler, payloads);

        // A confirmed observation emits `payment.observed` and
        // `payment.completed` together, which is the shipped completion path.
        var completed = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                completing.Payment!.PaymentId,
                "btc",
                "btc-test-address",
                "tx-confirmed-123",
                "0.00039980",
                ObservationTime,
                Confirmations: 1,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);
        Assert.Equal(RecordBlockchainObservationResultKind.Completed, completed.Kind);
        await DrainAsync(serviceProvider, handler, payloads);

        clock.UtcNow = AfterLateAcceptance;
        var expiration = await payments.ExpireDuePaymentsAsync(10, CancellationToken.None);
        Assert.Equal(1, expiration.ExpiredCount);
        await DrainAsync(serviceProvider, handler, payloads);

        await SettleAsync(serviceProvider, expiring.Payment.PaymentId);
        await DrainAsync(serviceProvider, handler, payloads);

        return payloads;
    }

    private static string PayerPageIdOf(CreatePaymentResult result) =>
        result.Payment!.PayerPageUrl.Split('/')[^1];

    private static async Task DrainAsync(
        IServiceProvider serviceProvider,
        CapturingHttpMessageHandler handler,
        Dictionary<string, string> payloads)
    {
        while (await ProcessNextAsync(serviceProvider))
        {
            // The first delivered payload of a type is the accepted example;
            // later Payments repeat the same shape.
            payloads.TryAdd(handler.LastEventType!, NormalizeJson(handler.LastBody!));
        }
    }

    private static async Task<bool> ProcessNextAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
        return await processor.ProcessNextAsync(CancellationToken.None);
    }

    private static async Task SettleAsync(IServiceProvider serviceProvider, Guid paymentId)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var version = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.Id == paymentId)
            .Select(payment => payment.Version)
            .SingleAsync();

        var admin = scope.ServiceProvider.GetRequiredService<AdminPaymentQueryService>();
        var result = await admin.SettleAsync(
            paymentId,
            version,
            "Payer confirmed the transfer out of band.",
            new AdminOperationContext(
                AdminAccountId,
                SourceIp: null,
                UserAgent: null,
                // A UUID so the snapshot normalizes it like every other
                // correlation identifier the product generates.
                Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        Assert.Equal(AdminPaymentSettlementResultKind.Settled, result.Kind);
    }

    private static ServiceProvider BuildServiceProvider(
        string connectionString,
        CapturingHttpMessageHandler handler,
        IClock clock)
    {
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton(clock);
        services.AddSingleton<IPayerPageIdGenerator, SequentialPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddScoped<IBlockchainObservationAdapter, NoOpBlockchainObservationAdapter>();
        services.AddScoped<IWebhookSecretResolver, FixedWebhookSecretResolver>();
        services.Configure<WebhookDeliveryOptions>(options =>
        {
            options.MaxAttempts = 5;
            options.RetryDelay = TimeSpan.FromMinutes(1);
            options.RetryJitterRatio = 0;
        });
        services.AddScoped(serviceProvider => new WebhookDeliveryProcessor(
            serviceProvider.GetRequiredService<PayaffeDbContext>(),
            new HttpClient(handler),
            serviceProvider.GetRequiredService<IWebhookSecretResolver>(),
            serviceProvider.GetRequiredService<IClock>(),
            serviceProvider.GetRequiredService<IOptions<WebhookDeliveryOptions>>()));
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PayerPageBaseUrl = "https://pay.example.test/pay";
            options.PaymentExpiration = TimeSpan.FromHours(1);
            options.LateAcceptanceWindow = TimeSpan.FromHours(24);
        });

        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Contract test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("valid-token"),
            Status = "active",
            CreatedAt = LifecycleStart,
            UpdatedAt = LifecycleStart,
        });
        dbContext.AdminAccounts.Add(new AdminAccountRecord
        {
            Id = AdminAccountId,
            Username = "contract-operator",
            NormalizedUsername = "contract-operator",
            PasswordHash = "not-used-in-this-test",
            Status = "active",
            CreatedAt = LifecycleStart,
            UpdatedAt = LifecycleStart,
        });
        dbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
        {
            Id = Guid.NewGuid(),
            IntegrationApiCredentialId = CredentialId,
            Url = "https://receiver.example.test/webhooks/payaffe",
            SecretReference = "secret://webhooks/test-endpoint",
            Status = "active",
            EventTypes = null,
            CreatedAt = LifecycleStart,
            UpdatedAt = LifecycleStart,
        });

        await dbContext.SaveChangesAsync();
    }

    private static string SnapshotFileName(string eventType) =>
        $"{eventType.Replace('.', '-')}.v1.json";

    private static bool ShouldUpdateSnapshot()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("PAYAFFE_UPDATE_CONTRACT_SNAPSHOTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes the values that differ between runs so contract drift stays
    /// focused on the envelope and the Payment snapshot shape.
    /// </summary>
    private static string NormalizeJson(string json)
    {
        var normalized = UuidRegex.Replace(json, NormalizedUuid);
        using var document = JsonDocument.Parse(normalized);
        var serialized = JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });

        return Regex.Replace(
            serialized,
            "(\"payer_page_id\": \")[^\"]*(\")",
            $"$1{NormalizedPayerPageId}$2") + Environment.NewLine;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Payaffe.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        public string? LastEventType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastEventType = request.Headers.GetValues("Payaffe-Webhook-Event-Type").Single();
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class SequentialPayerPageIdGenerator : IPayerPageIdGenerator
    {
        private int _next;

        public string Generate() => $"payer-page-{Interlocked.Increment(ref _next)}";
    }

    private sealed class FixedWebhookSecretResolver : IWebhookSecretResolver
    {
        public Task<string?> ResolveAsync(string secretReference, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("top-secret");
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
