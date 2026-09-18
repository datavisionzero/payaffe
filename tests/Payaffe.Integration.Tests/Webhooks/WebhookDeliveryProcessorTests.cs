using System.Net;
using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Webhooks;
using Payaffe.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Webhooks;

public sealed class WebhookDeliveryProcessorTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid CredentialId = Guid.Parse("78d8a09b-1c4a-4b6e-9f94-f8acbd4278f1");
    private static readonly DateTimeOffset ProcessorNow = DateTimeOffset.Parse("2026-07-04T12:30:00Z");

    [Fact]
    public async Task ProcessNextAsync_sends_signed_webhook_and_marks_event_delivered()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);

        var processed = await context.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("delivered", webhookEvent.Status);
        Assert.Equal(1, webhookEvent.AttemptCount);
        Assert.Null(webhookEvent.LastErrorCode);

        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal(ProjectDefaults.DefaultProjectId, attempt.ProjectId);
        Assert.Equal("succeeded", attempt.Result);
        Assert.Equal(204, attempt.HttpStatusCode);
        Assert.Equal(1, attempt.AttemptNumber);

        Assert.NotNull(context.Handler.Request);
        Assert.Equal("POST", context.Handler.Request.Method.Method);
        Assert.Equal("payment.created", context.Handler.Request.Headers.GetValues("Payaffe-Webhook-Event-Type").Single());
        Assert.Equal("1", context.Handler.Request.Headers.GetValues("Payaffe-Webhook-Event-Version").Single());
        Assert.Equal(ProcessorNow.ToUnixTimeSeconds().ToString(), context.Handler.Request.Headers.GetValues("Payaffe-Webhook-Timestamp").Single());
        Assert.Contains("\"event_id\"", context.Handler.RawBody, StringComparison.Ordinal);
        Assert.Contains("\"payment\"", context.Handler.RawBody, StringComparison.Ordinal);
        Assert.Equal(
            WebhookSignatureService.CreateSignature("top-secret", ProcessorNow, context.Handler.RawBody!),
            context.Handler.Request.Headers.GetValues("Payaffe-Webhook-Signature").Single());
    }

    [Fact]
    public async Task ProcessNextAsync_records_retry_for_retryable_http_failure()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.ServiceUnavailable);

        var processed = await context.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("retry_pending", webhookEvent.Status);
        Assert.Equal(1, webhookEvent.AttemptCount);
        Assert.Equal("http.503", webhookEvent.LastErrorCode);
        Assert.Equal(ProcessorNow.AddMinutes(1), webhookEvent.NextAttemptAt);

        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal("retry_pending", attempt.Result);
        Assert.Equal(503, attempt.HttpStatusCode);
        Assert.Equal("http.503", attempt.SafeErrorCode);
        Assert.Equal(ProcessorNow.AddMinutes(1), attempt.NextRetryAt);
    }

    [Fact]
    public async Task ProcessNextAsync_continues_existing_delivery_for_disabled_project()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        var project = Assert.Single(context.DbContext.Projects);
        project.Status = "disabled";
        project.UpdatedAt = ProcessorNow;
        project.Version++;
        await context.DbContext.SaveChangesAsync();

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal("delivered", Assert.Single(context.DbContext.WebhookOutboxEvents).Status);
        Assert.Equal(ProjectDefaults.DefaultProjectId, Assert.Single(context.DbContext.WebhookDeliveryAttempts).ProjectId);
    }

    [Fact]
    public async Task ProcessNextAsync_uses_configurable_exponential_retry_backoff()
    {
        await using var context = await BuildContextAsync(
            HttpStatusCode.ServiceUnavailable,
            options =>
            {
                options.RetryDelay = TimeSpan.FromMinutes(1);
                options.RetryBackoffMultiplier = 3;
                options.MaxRetryDelay = TimeSpan.FromMinutes(2);
                options.RetryJitterRatio = 0;
            });
        var firstProcessed = await context.Processor.ProcessNextAsync(CancellationToken.None);
        Assert.True(firstProcessed);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal(ProcessorNow.AddMinutes(1), webhookEvent.NextAttemptAt);
        webhookEvent.NextAttemptAt = ProcessorNow;
        await context.DbContext.SaveChangesAsync();

        var secondProcessed = await context.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.True(secondProcessed);
        Assert.Equal(2, context.Handler.RequestCount);
        Assert.Equal("retry_pending", webhookEvent.Status);
        Assert.Equal(2, webhookEvent.AttemptCount);
        Assert.Equal(ProcessorNow.AddMinutes(2), webhookEvent.NextAttemptAt);
        var attempts = context.DbContext.WebhookDeliveryAttempts
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(ProcessorNow.AddMinutes(1), attempts[0].NextRetryAt);
        Assert.Equal(ProcessorNow.AddMinutes(2), attempts[1].NextRetryAt);
    }

    [Fact]
    public async Task ResendAsync_sends_failed_webhook_event_immediately()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.ServiceUnavailable);
        var initialProcessed = await context.Processor.ProcessNextAsync(CancellationToken.None);
        Assert.True(initialProcessed);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("retry_pending", webhookEvent.Status);
        Assert.Equal(1, webhookEvent.AttemptCount);
        Assert.Equal(1, context.Handler.RequestCount);
        context.Handler.ResponseStatusCode = HttpStatusCode.NoContent;

        var result = await context.Processor.ResendAsync(webhookEvent.ProjectId, webhookEvent.Id, CancellationToken.None);

        Assert.Equal(WebhookManualResendResultKind.Resent, result.Kind);
        Assert.Equal("delivered", result.Status);
        Assert.Equal(2, context.Handler.RequestCount);
        context.DbContext.ChangeTracker.Clear();
        var resentEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("delivered", resentEvent.Status);
        Assert.Equal(2, resentEvent.AttemptCount);
        Assert.Null(resentEvent.LastErrorCode);
        var attempts = context.DbContext.WebhookDeliveryAttempts
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal("retry_pending", attempts[0].Result);
        Assert.Equal("succeeded", attempts[1].Result);
        Assert.Equal(2, attempts[1].AttemptNumber);
    }

    [Fact]
    public async Task ResendAsync_rejects_non_failed_webhook_event()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);

        var result = await context.Processor.ResendAsync(
            Assert.Single(context.DbContext.WebhookOutboxEvents).ProjectId,
            Assert.Single(context.DbContext.WebhookOutboxEvents).Id,
            CancellationToken.None);

        Assert.Equal(WebhookManualResendResultKind.NotResendable, result.Kind);
        Assert.Equal("pending", result.Status);
        Assert.Equal(0, context.Handler.RequestCount);
        Assert.Empty(context.DbContext.WebhookDeliveryAttempts);
    }

    [Fact]
    public async Task ProcessNextAsync_records_terminal_failure_for_non_retryable_http_failure()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.BadRequest);

        var processed = await context.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("terminal_failed", webhookEvent.Status);
        Assert.Equal(1, webhookEvent.AttemptCount);
        Assert.Equal("http.400", webhookEvent.LastErrorCode);

        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal("terminal_failed", attempt.Result);
        Assert.Equal(400, attempt.HttpStatusCode);
        Assert.Equal("http.400", attempt.SafeErrorCode);
        Assert.Null(attempt.NextRetryAt);
    }

    [Fact]
    public async Task HostedService_processes_initial_batch_when_enabled()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        using var worker = new WebhookDeliveryHostedService(
            context.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WebhookDeliveryOptions
            {
                Enabled = true,
                MaxAttempts = 5,
                RetryDelay = TimeSpan.FromMinutes(1),
                RetryJitterRatio = 0,
                PollInterval = TimeSpan.FromHours(1),
                MaxEventsPerPoll = 5,
            }),
            NullLogger<WebhookDeliveryHostedService>.Instance,
            SchemaMigrationState.AlreadyApplied());

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await context.Handler.WaitForRequestAsync(TimeSpan.FromSeconds(5));
            await WaitForWebhookEventStatusAsync(context.DbContext, "delivered");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        context.DbContext.ChangeTracker.Clear();
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("delivered", webhookEvent.Status);
        Assert.Equal(1, webhookEvent.AttemptCount);
    }

    private static async Task WaitForWebhookEventStatusAsync(
        PayaffeDbContext dbContext,
        string expectedStatus)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            dbContext.ChangeTracker.Clear();
            var webhookEvent = Assert.Single(dbContext.WebhookOutboxEvents);
            if (webhookEvent.Status == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        dbContext.ChangeTracker.Clear();
        var finalEvent = Assert.Single(dbContext.WebhookOutboxEvents);
        Assert.Equal(expectedStatus, finalEvent.Status);
    }

    [Fact]
    public async Task ProcessNextAsync_clears_the_event_lease_after_delivery()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("delivered", webhookEvent.Status);
        Assert.Null(webhookEvent.LockedBy);
        Assert.Null(webhookEvent.LockedUntil);
    }

    [Fact]
    public async Task ProcessNextAsync_skips_an_event_leased_by_another_worker()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        await LeaseEventAsync(context, "other-worker", ProcessorNow.AddMinutes(1));

        var processed = await context.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.False(processed);
        Assert.Equal(0, context.Handler.RequestCount);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("pending", webhookEvent.Status);
        Assert.Equal(0, webhookEvent.AttemptCount);
        Assert.Equal("other-worker", webhookEvent.LockedBy);
    }

    [Fact]
    public async Task ProcessNextAsync_recovers_an_event_whose_lease_expired()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        await LeaseEventAsync(context, "crashed-worker", ProcessorNow.AddMinutes(-1));

        var processed = await context.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(1, context.Handler.RequestCount);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("delivered", webhookEvent.Status);
        Assert.Null(webhookEvent.LockedBy);
    }

    [Fact]
    public async Task Concurrent_workers_do_not_claim_the_same_event_twice()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);

        await using var firstScope = context.ServiceProvider.CreateAsyncScope();
        await using var secondScope = context.ServiceProvider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
        var second = secondScope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();

        Assert.True(await first.ProcessNextAsync(CancellationToken.None));
        Assert.False(await second.ProcessNextAsync(CancellationToken.None));

        Assert.Equal(1, context.Handler.RequestCount);
        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal(1, attempt.AttemptNumber);
    }

    private static async Task LeaseEventAsync(
        ProcessorContext context,
        string owner,
        DateTimeOffset lockedUntil)
    {
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        webhookEvent.LockedBy = owner;
        webhookEvent.LockedUntil = lockedUntil;
        await context.DbContext.SaveChangesAsync();
        context.DbContext.ChangeTracker.Clear();
    }

    private async Task<ProcessorContext> BuildContextAsync(
        HttpStatusCode responseStatusCode,
        Action<WebhookDeliveryOptions>? configureWebhookDelivery = null)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var handler = new CapturingHttpMessageHandler(responseStatusCode);
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddScoped<IBlockchainObservationAdapter, NoOpBlockchainObservationAdapter>();
        services.AddScoped<IWebhookSecretResolver, FixedWebhookSecretResolver>();
        services.Configure<WebhookDeliveryOptions>(options =>
        {
            options.MaxAttempts = 5;
            options.RetryDelay = TimeSpan.FromMinutes(1);
            options.RetryJitterRatio = 0;
            configureWebhookDelivery?.Invoke(options);
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
        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);
        await SeedCredentialAsync(serviceProvider, "valid-token");

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
        dbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
        {
            Id = Guid.NewGuid(),
            IntegrationApiCredentialId = CredentialId,
            Url = "https://receiver.example.test/webhooks/payaffe",
            SecretReference = "secret://webhooks/test-endpoint",
            Status = "active",
            EventTypes = null,
            CreatedAt = ProcessorNow,
            UpdatedAt = ProcessorNow,
        });
        await dbContext.SaveChangesAsync();

        var processor = serviceProvider.GetRequiredService<WebhookDeliveryProcessor>();

        return new ProcessorContext(serviceProvider, dbContext, processor, handler);
    }

    private static async Task SeedCredentialAsync(IServiceProvider serviceProvider, string token)
    {
        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken(token),
            Status = "active",
            CreatedAt = ProcessorNow,
            UpdatedAt = ProcessorNow,
        });

        await dbContext.SaveChangesAsync();
    }

    private sealed record ProcessorContext(
        ServiceProvider ServiceProvider,
        PayaffeDbContext DbContext,
        WebhookDeliveryProcessor Processor,
        CapturingHttpMessageHandler Handler) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ServiceProvider.DisposeAsync();
        }
    }

    private sealed class CapturingHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _requestCaptured = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpStatusCode ResponseStatusCode { get; set; } = statusCode;

        public int RequestCount { get; private set; }

        public HttpRequestMessage? Request { get; private set; }

        public string? RawBody { get; private set; }

        public async Task WaitForRequestAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await _requestCaptured.Task.WaitAsync(cts.Token);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            RawBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            _requestCaptured.TrySetResult();
            return new HttpResponseMessage(ResponseStatusCode);
        }
    }

    private sealed class FixedWebhookSecretResolver : IWebhookSecretResolver
    {
        public Task<string?> ResolveAsync(
            string secretReference,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>("top-secret");
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => ProcessorNow;
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
}
