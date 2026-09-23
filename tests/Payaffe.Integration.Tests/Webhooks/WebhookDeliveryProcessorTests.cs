using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Infrastructure.Webhooks;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public async Task ProcessNextAsync_records_retry_when_receiver_times_out()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        context.Handler.SimulateTimeout = true;

        var processed = await context.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("retry_pending", webhookEvent.Status);
        Assert.Equal(1, webhookEvent.AttemptCount);
        Assert.Equal("http.timeout", webhookEvent.LastErrorCode);
        Assert.Equal(ProcessorNow.AddMinutes(1), webhookEvent.NextAttemptAt);
        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal("retry_pending", attempt.Result);
        Assert.Equal("http.timeout", attempt.SafeErrorCode);
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

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/hooks")]
    [InlineData("http://192.168.1.1/hooks")]
    [InlineData("http://100.64.0.1/hooks")]
    [InlineData("http://[fe80::1]/hooks")]
    [InlineData("http://[fd00::1]/hooks")]
    [InlineData("http://0.0.0.0/hooks")]
    public async Task Delivery_refuses_a_private_or_link_local_target(string endpointUrl)
    {
        await using var context = await BuildContextAsync(
            HttpStatusCode.NoContent,
            endpointUrl: endpointUrl,
            useDeliveryHandler: true);

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        AssertRefused(context);
    }

    [Fact]
    public async Task Delivery_refuses_a_loopback_target_without_connecting()
    {
        await using var receiver = LoopbackReceiver.Start(port => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var context = await BuildContextAsync(
            HttpStatusCode.NoContent,
            endpointUrl: $"http://127.0.0.1:{receiver.Port}/hooks",
            useDeliveryHandler: true);

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        AssertRefused(context);
        Assert.Equal(0, receiver.ConnectionCount);
    }

    [Fact]
    public async Task Delivery_refuses_a_host_name_that_resolves_to_loopback()
    {
        await using var receiver = LoopbackReceiver.Start(port => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var context = await BuildContextAsync(
            HttpStatusCode.NoContent,
            endpointUrl: $"http://localhost:{receiver.Port}/hooks",
            useDeliveryHandler: true);

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        AssertRefused(context);
        Assert.Equal(0, receiver.ConnectionCount);
    }

    [Fact]
    public async Task Delivery_reaches_an_allowlisted_private_target()
    {
        await using var receiver = LoopbackReceiver.Start(port => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var context = await BuildContextAsync(
            HttpStatusCode.NoContent,
            options => options.AllowedPrivateTargets = "localhost",
            endpointUrl: $"http://localhost:{receiver.Port}/hooks",
            useDeliveryHandler: true);

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal("delivered", Assert.Single(context.DbContext.WebhookOutboxEvents).Status);
        Assert.Equal(1, receiver.ConnectionCount);
    }

    [Fact]
    public async Task Delivery_treats_a_redirect_as_terminal_and_does_not_follow_it()
    {
        await using var receiver = LoopbackReceiver.Start(port =>
            $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{port}/elsewhere\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var context = await BuildContextAsync(
            HttpStatusCode.NoContent,
            options => options.AllowedPrivateTargets = "127.0.0.1",
            endpointUrl: $"http://127.0.0.1:{receiver.Port}/hooks",
            useDeliveryHandler: true);

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("terminal_failed", webhookEvent.Status);
        Assert.Equal("http.307", webhookEvent.LastErrorCode);
        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal(307, attempt.HttpStatusCode);
        Assert.Equal(1, receiver.ConnectionCount);
        Assert.Equal(["POST /hooks HTTP/1.1"], receiver.RequestLines);
    }

    private static void AssertRefused(ProcessorContext context)
    {
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("terminal_failed", webhookEvent.Status);
        Assert.Equal(WebhookTargetPolicy.RefusedErrorCode, webhookEvent.LastErrorCode);
        Assert.Equal(1, webhookEvent.AttemptCount);
        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal("terminal_failed", attempt.Result);
        Assert.Null(attempt.HttpStatusCode);
        Assert.Equal(WebhookTargetPolicy.RefusedErrorCode, attempt.SafeErrorCode);
        Assert.Null(attempt.NextRetryAt);
    }

    /// <summary>
    /// A raw socket server, so a test can count connections and see whether a
    /// redirect was followed without any HTTP stack on the receiving side.
    /// </summary>
    private sealed class LoopbackReceiver : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<int, string> _response;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _acceptLoop;
        private int _connectionCount;

        private LoopbackReceiver(Func<int, string> response)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _response = response;
            _acceptLoop = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public ConcurrentQueue<string> RequestLines { get; } = new();

        public static LoopbackReceiver Start(Func<int, string> response) => new(response);

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    Interlocked.Increment(ref _connectionCount);
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var requestLine = await reader.ReadLineAsync(_stopping.Token);
                    if (requestLine is not null)
                    {
                        RequestLines.Enqueue(requestLine);
                    }

                    var bytes = Encoding.ASCII.GetBytes(_response(Port));
                    await stream.WriteAsync(bytes, _stopping.Token);
                    await stream.FlushAsync(_stopping.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            await _acceptLoop;
            _stopping.Dispose();
        }
    }

    [Fact]
    public async Task A_poison_event_counts_as_a_failed_attempt_and_the_next_event_is_still_delivered()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        var (poison, healthy) = await AddPoisonEventAsync(context);

        Assert.Equal(WebhookEventProcessing.Failed, await context.Processor.ProcessNextEventAsync(CancellationToken.None));
        Assert.Equal(WebhookEventProcessing.Processed, await context.Processor.ProcessNextEventAsync(CancellationToken.None));

        context.DbContext.ChangeTracker.Clear();
        var poisonEvent = context.DbContext.WebhookOutboxEvents.Single(webhookEvent => webhookEvent.Id == poison);
        Assert.Equal("retry_pending", poisonEvent.Status);
        Assert.Equal(1, poisonEvent.AttemptCount);
        Assert.Equal(WebhookDeliveryProcessor.ProcessingFailedErrorCode, poisonEvent.LastErrorCode);
        Assert.Equal(ProcessorNow.AddMinutes(1), poisonEvent.NextAttemptAt);
        Assert.Null(poisonEvent.LockedBy);
        var poisonAttempt = context.DbContext.WebhookDeliveryAttempts.Single(attempt => attempt.WebhookEventId == poison);
        Assert.Equal("retry_pending", poisonAttempt.Result);
        Assert.Equal(WebhookDeliveryProcessor.ProcessingFailedErrorCode, poisonAttempt.SafeErrorCode);
        Assert.Equal(
            "delivered",
            context.DbContext.WebhookOutboxEvents.Single(webhookEvent => webhookEvent.Id == healthy).Status);
        Assert.Equal(1, context.Handler.RequestCount);
    }

    [Fact]
    public async Task A_poison_event_becomes_terminal_when_its_attempts_run_out()
    {
        await using var context = await BuildContextAsync(
            HttpStatusCode.NoContent,
            options => options.MaxAttempts = 1);
        var (poison, _) = await AddPoisonEventAsync(context);

        Assert.Equal(WebhookEventProcessing.Failed, await context.Processor.ProcessNextEventAsync(CancellationToken.None));

        context.DbContext.ChangeTracker.Clear();
        var poisonEvent = context.DbContext.WebhookOutboxEvents.Single(webhookEvent => webhookEvent.Id == poison);
        Assert.Equal("terminal_failed", poisonEvent.Status);
        Assert.Equal(1, poisonEvent.AttemptCount);
        Assert.Null(poisonEvent.LockedBy);
    }

    [Fact]
    public async Task HostedService_delivers_past_a_poison_event_and_reports_the_failure()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        var (poison, healthy) = await AddPoisonEventAsync(context);
        using var worker = new WebhookDeliveryHostedService(
            context.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WebhookDeliveryOptions
            {
                Enabled = true,
                PollInterval = TimeSpan.FromHours(1),
                MaxEventsPerPoll = 5,
            }),
            NullLogger<WebhookDeliveryHostedService>.Instance,
            SchemaMigrationState.AlreadyApplied());

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await context.Handler.WaitForRequestAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() =>
            {
                context.DbContext.ChangeTracker.Clear();
                return context.DbContext.BackgroundWorkerLeases.Any(lease =>
                    lease.WorkerName == "webhook-delivery" && lease.LastFailedAt != null);
            });
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        context.DbContext.ChangeTracker.Clear();
        Assert.Equal("delivered", context.DbContext.WebhookOutboxEvents.Single(webhookEvent => webhookEvent.Id == healthy).Status);
        Assert.Equal("retry_pending", context.DbContext.WebhookOutboxEvents.Single(webhookEvent => webhookEvent.Id == poison).Status);
        var lease = context.DbContext.BackgroundWorkerLeases.Single(record => record.WorkerName == "webhook-delivery");
        Assert.Equal(1, lease.ConsecutiveFailureCount);
        Assert.Equal(WebhookDeliveryProcessor.ProcessingFailedErrorCode, lease.LastSafeErrorCode);
        Assert.NotNull(lease.LastFailedAt);
    }

    [Fact]
    public async Task A_worker_that_lost_its_lease_does_not_overwrite_the_new_owner()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);
        var takeoverUntil = ProcessorNow.AddMinutes(5);
        context.Handler.OnSend = async () =>
        {
            await using var scope = context.ServiceProvider.CreateAsyncScope();
            var other = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            await other.WebhookOutboxEvents.ExecuteUpdateAsync(setters => setters
                .SetProperty(webhookEvent => webhookEvent.LockedBy, "other-worker")
                .SetProperty(webhookEvent => webhookEvent.LockedUntil, takeoverUntil));
        };

        Assert.Equal(WebhookEventProcessing.Processed, await context.Processor.ProcessNextEventAsync(CancellationToken.None));

        context.DbContext.ChangeTracker.Clear();
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("pending", webhookEvent.Status);
        Assert.Equal(0, webhookEvent.AttemptCount);
        Assert.Equal("other-worker", webhookEvent.LockedBy);
        Assert.Equal(takeoverUntil, webhookEvent.LockedUntil);
        Assert.Empty(context.DbContext.WebhookDeliveryAttempts);
    }

    [Fact]
    public async Task A_slow_receiver_is_cut_off_at_the_request_timeout()
    {
        await using var context = await BuildContextAsync(
            HttpStatusCode.NoContent,
            options => options.RequestTimeout = TimeSpan.FromSeconds(1));
        context.Handler.Delay = TimeSpan.FromSeconds(30);

        var started = DateTimeOffset.UtcNow;
        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10));
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("retry_pending", webhookEvent.Status);
        Assert.Equal("http.timeout", webhookEvent.LastErrorCode);
    }

    [Fact]
    public void The_delivery_client_times_out_at_the_configured_request_timeout()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure("Host=localhost;Database=payaffe");
        services.Configure<WebhookDeliveryOptions>(options => options.RequestTimeout = TimeSpan.FromSeconds(7));

        using var serviceProvider = services.BuildServiceProvider();
        using var client = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(WebhookDeliveryProcessor));

        Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
    }

    /// <summary>
    /// Adds a second, healthy Payment and turns the first Payment's event into
    /// one that fails every time: its observed amount cannot be parsed, so the
    /// payload can never be built. The poison event is due first.
    /// </summary>
    private static async Task<(Guid Poison, Guid Healthy)> AddPoisonEventAsync(ProcessorContext context)
    {
        var poison = Assert.Single(context.DbContext.WebhookOutboxEvents);
        var payments = context.ServiceProvider.GetRequiredService<PaymentApplicationService>();
        await payments.CreateAsync(
            CredentialId,
            new CreatePaymentCommand(
                "EUR",
                2999,
                "order-456",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-456"),
            CancellationToken.None);
        context.DbContext.ChangeTracker.Clear();
        var healthy = context.DbContext.WebhookOutboxEvents.Single(webhookEvent => webhookEvent.Id != poison.Id).Id;

        context.DbContext.MatchingBlockchainTransactions.Add(new MatchingBlockchainTransactionRecord
        {
            Id = Guid.NewGuid(),
            PaymentId = poison.PaymentId,
            SupportedCurrency = "BTC",
            PaymentAddress = "btc-test-address",
            TransactionHash = "poison-transaction",
            ObservedAmount = "not-a-number",
            ObservedAt = ProcessorNow,
            FirstObservedAt = ProcessorNow,
            ProviderName = "test",
            LastCheckedAt = ProcessorNow,
            CreatedAt = ProcessorNow,
            UpdatedAt = ProcessorNow,
        });
        await context.DbContext.SaveChangesAsync();
        await context.DbContext.WebhookOutboxEvents
            .Where(webhookEvent => webhookEvent.Id == poison.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                webhookEvent => webhookEvent.NextAttemptAt,
                ProcessorNow.AddMinutes(-1)));
        context.DbContext.ChangeTracker.Clear();
        return (poison.Id, healthy);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline && !condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task ResendAsync_is_refused_while_a_worker_holds_the_event_lease()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.ServiceUnavailable);
        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));
        await LeaseEventAsync(context, "other-worker", ProcessorNow.AddMinutes(1));
        context.Handler.ResponseStatusCode = HttpStatusCode.Gone;
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);

        var result = await context.Processor.ResendAsync(webhookEvent.ProjectId, webhookEvent.Id, CancellationToken.None);

        Assert.Equal(WebhookManualResendResultKind.InProgress, result.Kind);
        Assert.Equal(1, context.Handler.RequestCount);
        context.DbContext.ChangeTracker.Clear();
        var unchanged = Assert.Single(context.DbContext.WebhookOutboxEvents);
        Assert.Equal("retry_pending", unchanged.Status);
        Assert.Equal(1, unchanged.AttemptCount);
        Assert.Equal("other-worker", unchanged.LockedBy);
        Assert.Single(context.DbContext.WebhookDeliveryAttempts);
    }

    [Fact]
    public async Task ResendAsync_is_refused_while_a_worker_is_claiming_the_event()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.ServiceUnavailable);
        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);

        await using var claimScope = context.ServiceProvider.CreateAsyncScope();
        var claimContext = claimScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        await using var claim = await claimContext.Database.BeginTransactionAsync();
        await claimContext.WebhookOutboxEvents
            .FromSqlInterpolated($"select * from outbox.webhook_events where id = {webhookEvent.Id} for update")
            .ToListAsync();

        var result = await context.Processor.ResendAsync(webhookEvent.ProjectId, webhookEvent.Id, CancellationToken.None);

        Assert.Equal(WebhookManualResendResultKind.InProgress, result.Kind);
        Assert.Equal(1, context.Handler.RequestCount);
        await claim.RollbackAsync();
    }

    [Fact]
    public async Task ResendAsync_takes_the_lease_so_a_worker_cannot_claim_the_event_meanwhile()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.ServiceUnavailable);
        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));
        var webhookEvent = Assert.Single(context.DbContext.WebhookOutboxEvents);
        webhookEvent.NextAttemptAt = ProcessorNow;
        await context.DbContext.SaveChangesAsync();
        context.Handler.ResponseStatusCode = HttpStatusCode.NoContent;
        bool? workerClaimed = null;
        context.Handler.OnSend = async () =>
        {
            context.Handler.OnSend = null;
            await using var workerScope = context.ServiceProvider.CreateAsyncScope();
            var worker = workerScope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
            workerClaimed = await worker.ProcessNextAsync(CancellationToken.None);
        };

        var result = await context.Processor.ResendAsync(webhookEvent.ProjectId, webhookEvent.Id, CancellationToken.None);

        Assert.Equal(WebhookManualResendResultKind.Resent, result.Kind);
        Assert.Equal("delivered", result.Status);
        Assert.False(workerClaimed);
        Assert.Equal(2, context.Handler.RequestCount);
        context.DbContext.ChangeTracker.Clear();
        var attempts = context.DbContext.WebhookDeliveryAttempts.OrderBy(attempt => attempt.AttemptNumber).ToArray();
        Assert.Equal([1, 2], attempts.Select(attempt => attempt.AttemptNumber));
        Assert.Null(Assert.Single(context.DbContext.WebhookOutboxEvents).LockedBy);
    }

    [Fact]
    public async Task The_delivery_id_header_is_the_stored_attempt_id()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.NoContent);

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        var attempt = Assert.Single(context.DbContext.WebhookDeliveryAttempts);
        Assert.Equal(
            attempt.Id.ToString("D"),
            context.Handler.Request!.Headers.GetValues("Payaffe-Webhook-Id").Single());
    }

    [Fact]
    public async Task A_terminal_outcome_counts_one_terminal_transition()
    {
        await using var context = await BuildContextAsync(HttpStatusCode.BadRequest);
        long transitions = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (instrument.Meter.Name == PayaffeTelemetry.MeterName &&
                instrument.Name == "payaffe.webhook.delivery.terminal")
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref transitions, measurement));
        listener.Start();

        Assert.True(await context.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal("terminal_failed", Assert.Single(context.DbContext.WebhookOutboxEvents).Status);
        // The meter is process-global and other test classes run alongside,
        // so this can only be a lower bound.
        Assert.True(Interlocked.Read(ref transitions) >= 1);
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
        Action<WebhookDeliveryOptions>? configureWebhookDelivery = null,
        string endpointUrl = "https://receiver.example.test/webhooks/payaffe",
        bool useDeliveryHandler = false)
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
        if (!useDeliveryHandler)
        {
            services.AddScoped(serviceProvider => new WebhookDeliveryProcessor(
                serviceProvider.GetRequiredService<PayaffeDbContext>(),
                new HttpClient(handler),
                serviceProvider.GetRequiredService<IWebhookSecretResolver>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<IOptions<WebhookDeliveryOptions>>()));
        }

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
            Url = endpointUrl,
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

        public bool SimulateTimeout { get; set; }

        public TimeSpan Delay { get; set; }

        public Func<Task>? OnSend { get; set; }

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
            if (OnSend is not null)
            {
                await OnSend();
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (SimulateTimeout)
            {
                throw new TaskCanceledException("Simulated receiver timeout.");
            }

            return new HttpResponseMessage(ResponseStatusCode);
        }
    }

    private sealed class FixedWebhookSecretResolver : IWebhookSecretResolver
    {
        public Task<string?> ResolveForProjectAsync(
            Guid projectId,
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
        private int _generated;

        public string Generate() =>
            Interlocked.Increment(ref _generated) == 1 ? "fixed-payer-page-id" : $"fixed-payer-page-id-{_generated}";
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
