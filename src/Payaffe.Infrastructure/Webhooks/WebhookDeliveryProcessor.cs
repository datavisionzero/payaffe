using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Payaffe.Application.Installation;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Webhooks;

public sealed class WebhookDeliveryProcessor(
    PayaffeDbContext dbContext,
    HttpClient httpClient,
    IWebhookSecretResolver secretResolver,
    IClock clock,
    IOptions<WebhookDeliveryOptions> options,
    ConfiguredInstallationMode? installationMode = null,
    ILogger<WebhookDeliveryProcessor>? logger = null)
{
    /// <summary>
    /// The safe error code of an event whose processing failed for a reason
    /// other than the receiver's answer, such as a payload that cannot be built.
    /// </summary>
    public const string ProcessingFailedErrorCode = "webhook_delivery.processing_failed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WebhookDeliveryOptions _options = options.Value;

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    private readonly string _workerId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    /// <summary>The Webhook Endpoint of the delivery in progress, once it is known.</summary>
    private Guid? _deliveringEndpointId;

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken) =>
        await ProcessNextEventAsync(cancellationToken) != WebhookEventProcessing.None;

    /// <summary>
    /// Claims and delivers one due event. Whatever goes wrong with that one
    /// event is written to it as a failed attempt, so an event that fails
    /// every time runs out of attempts instead of being claimed first on every
    /// poll and starving the others.
    /// </summary>
    public async Task<WebhookEventProcessing> ProcessNextEventAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var webhookEvent = await ClaimNextAsync(now, cancellationToken);

        if (webhookEvent is null)
        {
            return WebhookEventProcessing.None;
        }

        try
        {
            await DeliverAsync(webhookEvent, cancellationToken);
            return WebhookEventProcessing.Processed;
        }
        catch (DbUpdateConcurrencyException)
        {
            LogLeaseLost(webhookEvent.Id);
            dbContext.ChangeTracker.Clear();
            return WebhookEventProcessing.Processed;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "Webhook Event {WebhookEventId} could not be processed and is counted as a failed attempt.",
                webhookEvent.Id);
            await RecordProcessingFailureAsync(webhookEvent.ProjectId, webhookEvent.Id, cancellationToken);
            return WebhookEventProcessing.Failed;
        }
    }

    /// <summary>
    /// Claims exactly one due Webhook Event so that concurrent workers cannot
    /// deliver the same event twice. An active lease is skipped and becomes
    /// claimable again once it expires, which recovers a crashed worker's event.
    /// Every later write to the event is conditional on still holding the
    /// lease (<c>locked_by</c> is a concurrency token), so a worker whose lease
    /// expired cannot overwrite the outcome or the lease of the one that took
    /// over.
    /// </summary>
    private async Task<WebhookOutboxEventRecord?> ClaimNextAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var candidate = await dbContext.WebhookOutboxEvents
                .Where(webhookEvent =>
                    (webhookEvent.Status == "pending" || webhookEvent.Status == "retry_pending") &&
                    webhookEvent.NextAttemptAt <= now &&
                    (webhookEvent.LockedUntil == null || webhookEvent.LockedUntil <= now))
                .OrderBy(webhookEvent => webhookEvent.NextAttemptAt)
                .ThenBy(webhookEvent => webhookEvent.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (candidate is null)
            {
                return null;
            }

            ApplyLease(candidate, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return candidate;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await dbContext.WebhookOutboxEvents
            .FromSqlInterpolated(
                $"""
                select * from outbox.webhook_events
                where status in ('pending', 'retry_pending')
                  and next_attempt_at <= {now}
                  and (locked_until is null or locked_until <= {now})
                order by next_attempt_at, created_at
                for update skip locked
                limit 1
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (claimed is not null)
        {
            ApplyLease(claimed, now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    private void ApplyLease(WebhookOutboxEventRecord webhookEvent, DateTimeOffset now)
    {
        var leaseDuration = _options.LeaseDuration > TimeSpan.Zero
            ? _options.LeaseDuration
            : TimeSpan.FromMinutes(2);
        webhookEvent.LockedBy = _workerId;
        webhookEvent.LockedUntil = now.Add(leaseDuration);
    }

    /// <summary>
    /// Delivers a failed event again at an Admin's request. The resend takes
    /// the same event lease the worker does, so it is refused while a worker
    /// or another resend holds the event instead of racing it and overwriting
    /// its outcome.
    /// </summary>
    public async Task<WebhookManualResendResult> ResendAsync(
        Guid projectId,
        Guid webhookEventId,
        CancellationToken cancellationToken)
    {
        var claim = await ClaimForResendAsync(projectId, webhookEventId, clock.UtcNow, cancellationToken);
        if (claim.Result is not null)
        {
            return claim.Result;
        }

        var webhookEvent = claim.Event!;
        try
        {
            await DeliverAsync(webhookEvent, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogLeaseLost(webhookEventId);
            dbContext.ChangeTracker.Clear();
            return WebhookManualResendResult.InProgress();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "Webhook Event {WebhookEventId} could not be resent and is counted as a failed attempt.",
                webhookEventId);
            await RecordProcessingFailureAsync(projectId, webhookEventId, cancellationToken);
            var failed = await dbContext.WebhookOutboxEvents
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.ProjectId == projectId && candidate.Id == webhookEventId,
                    cancellationToken);
            return WebhookManualResendResult.Resent(failed.Status);
        }

        return WebhookManualResendResult.Resent(webhookEvent.Status);
    }

    private async Task<(WebhookOutboxEventRecord? Event, WebhookManualResendResult? Result)> ClaimForResendAsync(
        Guid projectId,
        Guid webhookEventId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var candidate = await dbContext.WebhookOutboxEvents
                .SingleOrDefaultAsync(
                    webhookEvent => webhookEvent.ProjectId == projectId && webhookEvent.Id == webhookEventId,
                    cancellationToken);
            var refusal = RefuseResend(candidate, now);
            if (refusal is not null)
            {
                return (null, refusal);
            }

            ApplyLease(candidate!, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return (candidate, null);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await dbContext.WebhookOutboxEvents
            .FromSqlInterpolated(
                $"""
                select * from outbox.webhook_events
                where project_id = {projectId} and id = {webhookEventId}
                for update skip locked
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (claimed is null)
        {
            // Skipped rather than missing when a worker is claiming it right now.
            var exists = await dbContext.WebhookOutboxEvents
                .AsNoTracking()
                .AnyAsync(
                    webhookEvent => webhookEvent.ProjectId == projectId && webhookEvent.Id == webhookEventId,
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (null, exists ? WebhookManualResendResult.InProgress() : WebhookManualResendResult.NotFound());
        }

        var refused = RefuseResend(claimed, now);
        if (refused is null)
        {
            ApplyLease(claimed, now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return refused is null ? (claimed, null) : (null, refused);
    }

    private static WebhookManualResendResult? RefuseResend(WebhookOutboxEventRecord? webhookEvent, DateTimeOffset now)
    {
        if (webhookEvent is null)
        {
            return WebhookManualResendResult.NotFound();
        }

        if (webhookEvent.Status is not ("retry_pending" or "terminal_failed"))
        {
            return WebhookManualResendResult.NotResendable(webhookEvent.Status);
        }

        return webhookEvent.LockedUntil > now
            ? WebhookManualResendResult.InProgress()
            : null;
    }

    private async Task DeliverAsync(
        WebhookOutboxEventRecord webhookEvent,
        CancellationToken cancellationToken)
    {
        _deliveringEndpointId = null;
        var endpoints = await dbContext.WebhookEndpoints
            .Where(candidate =>
                candidate.ProjectId == webhookEvent.ProjectId &&
                candidate.IntegrationApiCredentialId == webhookEvent.IntegrationApiCredentialId &&
                candidate.Status == "active")
            .OrderBy(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);
        var endpoint = endpoints.FirstOrDefault(candidate => SupportsEventType(candidate, webhookEvent.EventType));
        if (endpoint is null)
        {
            await WriteOutcomeAsync(
                webhookEvent,
                attempt: null,
                "terminal_failed",
                "webhook_endpoint.not_found",
                nextAttemptAt: null,
                cancellationToken);
            return;
        }

        _deliveringEndpointId = endpoint.Id;
        var payment = await dbContext.Payments
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.ProjectId == webhookEvent.ProjectId &&
                             candidate.Id == webhookEvent.PaymentId,
                cancellationToken);
        string? secret;
        try
        {
            secret = await secretResolver.ResolveForProjectAsync(
                webhookEvent.ProjectId,
                endpoint.SecretReference,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ApplyRetryableFailureAsync(
                webhookEvent,
                endpoint,
                "webhook_secret.resolve_failed",
                cancellationToken);
            return;
        }
        if (string.IsNullOrWhiteSpace(secret))
        {
            await ApplyTerminalFailureAsync(
                webhookEvent,
                endpoint,
                "webhook_secret.unavailable",
                httpStatusCode: null,
                cancellationToken);
            return;
        }

        var payload = await BuildPayloadAsync(webhookEvent, payment, cancellationToken);
        var rawBody = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(rawBody, Encoding.UTF8, "application/json"),
        };
        // The delivery id a receiver sees is the id of the attempt row, so
        // it can be matched to Delivery history.
        var attemptId = Guid.NewGuid();
        var timestamp = clock.UtcNow;
        request.Headers.Add("Payaffe-Webhook-Id", attemptId.ToString("D"));
        request.Headers.Add("Payaffe-Webhook-Timestamp", timestamp.ToUnixTimeSeconds().ToString());
        request.Headers.Add("Payaffe-Webhook-Signature", WebhookSignatureService.CreateSignature(secret, timestamp, rawBody));
        request.Headers.Add("Payaffe-Webhook-Event-Type", webhookEvent.EventType);
        request.Headers.Add("Payaffe-Webhook-Event-Version", webhookEvent.EventVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpStatusCode statusCode;
        try
        {
            // Bounded here as well as on the client, so the request always
            // ends inside the event lease however the client was built.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            using var response = await httpClient.SendAsync(request, timeout.Token);
            statusCode = response.StatusCode;
        }
        catch (HttpRequestException exception) when (exception.InnerException is WebhookTargetRefusedException)
        {
            // Retrying cannot change where the name points often enough to be
            // worth it, and an Admin needs to see the reason, not a retry.
            await ApplyTerminalFailureAsync(
                webhookEvent,
                endpoint,
                WebhookTargetPolicy.RefusedErrorCode,
                httpStatusCode: null,
                cancellationToken,
                attemptId);
            return;
        }
        catch (HttpRequestException)
        {
            await ApplyRetryableFailureAsync(webhookEvent, endpoint, "http.request_failed", cancellationToken, attemptId: attemptId);
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await ApplyRetryableFailureAsync(webhookEvent, endpoint, "http.timeout", cancellationToken, attemptId: attemptId);
            return;
        }

        await ApplyHttpResultAsync(webhookEvent, endpoint, statusCode, attemptId, cancellationToken);
    }

    private async Task ApplyHttpResultAsync(
        WebhookOutboxEventRecord webhookEvent,
        WebhookEndpointRecord endpoint,
        HttpStatusCode statusCode,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        if ((int)statusCode >= 200 && (int)statusCode <= 299)
        {
            await WriteOutcomeAsync(
                webhookEvent,
                new AttemptOutcome(endpoint.Id, "succeeded", (int)statusCode, SafeErrorCode: null, NextRetryAt: null, attemptId),
                "delivered",
                safeErrorCode: null,
                nextAttemptAt: null,
                cancellationToken);
            return;
        }

        if (IsRetryable(statusCode))
        {
            await ApplyRetryableFailureAsync(webhookEvent, endpoint, $"http.{(int)statusCode}", cancellationToken, (int)statusCode, attemptId);
            return;
        }

        await ApplyTerminalFailureAsync(
            webhookEvent,
            endpoint,
            $"http.{(int)statusCode}",
            (int)statusCode,
            cancellationToken,
            attemptId);
    }

    private Task ApplyTerminalFailureAsync(
        WebhookOutboxEventRecord webhookEvent,
        WebhookEndpointRecord endpoint,
        string safeErrorCode,
        int? httpStatusCode,
        CancellationToken cancellationToken,
        Guid? attemptId = null) =>
        WriteOutcomeAsync(
            webhookEvent,
            new AttemptOutcome(endpoint.Id, "terminal_failed", httpStatusCode, safeErrorCode, NextRetryAt: null, attemptId),
            "terminal_failed",
            safeErrorCode,
            nextAttemptAt: null,
            cancellationToken);

    private Task ApplyRetryableFailureAsync(
        WebhookOutboxEventRecord webhookEvent,
        WebhookEndpointRecord endpoint,
        string safeErrorCode,
        CancellationToken cancellationToken,
        int? httpStatusCode = null,
        Guid? attemptId = null)
    {
        var (status, nextRetryAt) = NextRetry(webhookEvent);
        return WriteOutcomeAsync(
            webhookEvent,
            new AttemptOutcome(endpoint.Id, status, httpStatusCode, safeErrorCode, nextRetryAt, attemptId),
            status,
            safeErrorCode,
            nextRetryAt,
            cancellationToken);
    }

    private (string Status, DateTimeOffset? NextRetryAt) NextRetry(WebhookOutboxEventRecord webhookEvent)
    {
        var nextAttemptNumber = webhookEvent.AttemptCount + 1;
        return nextAttemptNumber >= _options.MaxAttempts
            ? ("terminal_failed", null)
            : ("retry_pending", clock.UtcNow.Add(CalculateRetryDelay(_options, nextAttemptNumber)));
    }

    /// <summary>
    /// Writes the outcome of one delivery: the attempt row, the event state,
    /// and the released lease, in one save. The update is conditional on the
    /// lease this processor took, so it fails as a whole with a
    /// <see cref="DbUpdateConcurrencyException"/> when another owner has taken
    /// the event over in the meantime.
    /// </summary>
    private async Task WriteOutcomeAsync(
        WebhookOutboxEventRecord webhookEvent,
        AttemptOutcome? attempt,
        string status,
        string? safeErrorCode,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (attempt is not null)
        {
            dbContext.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttemptRecord
            {
                ProjectId = webhookEvent.ProjectId,
                Id = attempt.Id ?? Guid.NewGuid(),
                WebhookEventId = webhookEvent.Id,
                WebhookEndpointId = attempt.EndpointId,
                AttemptNumber = webhookEvent.AttemptCount + 1,
                AttemptedAt = clock.UtcNow,
                Result = attempt.Result,
                HttpStatusCode = attempt.HttpStatusCode,
                SafeErrorCode = attempt.SafeErrorCode,
                NextRetryAt = attempt.NextRetryAt,
                CorrelationId = webhookEvent.CorrelationId,
            });
            webhookEvent.AttemptCount++;
        }

        webhookEvent.Status = status;
        webhookEvent.LastErrorCode = safeErrorCode;
        webhookEvent.NextAttemptAt = nextAttemptAt ?? webhookEvent.NextAttemptAt;
        webhookEvent.LockedBy = null;
        webhookEvent.LockedUntil = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (attempt is not null)
        {
            // Every attempt outcome passes through here, including manual
            // resend, so this is the one place the delivery counter has to be
            // updated.
            PayaffeTelemetry.RecordWebhookAttempt(attempt.Result);
        }

        if (status == "terminal_failed")
        {
            PayaffeTelemetry.RecordWebhookTerminalTransition();
        }
    }

    /// <summary>
    /// Counts an unexpected failure as an attempt with the usual backoff, so
    /// the event becomes terminal once attempts run out. The tracked state may
    /// be what failed to save, so the event is read again, and nothing is
    /// written when the lease is no longer this processor's.
    /// </summary>
    private async Task RecordProcessingFailureAsync(
        Guid projectId,
        Guid webhookEventId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var webhookEvent = await dbContext.WebhookOutboxEvents
            .SingleOrDefaultAsync(
                candidate => candidate.ProjectId == projectId && candidate.Id == webhookEventId,
                cancellationToken);
        if (webhookEvent is null || !StringComparer.Ordinal.Equals(webhookEvent.LockedBy, _workerId))
        {
            LogLeaseLost(webhookEventId);
            return;
        }

        var (status, nextRetryAt) = NextRetry(webhookEvent);
        var attempt = _deliveringEndpointId is { } endpointId
            ? new AttemptOutcome(endpointId, status, HttpStatusCode: null, ProcessingFailedErrorCode, nextRetryAt)
            : null;
        if (attempt is null)
        {
            // Without an Endpoint there is no attempt row to write, but the
            // failure still counts towards the attempt limit.
            webhookEvent.AttemptCount++;
        }

        try
        {
            await WriteOutcomeAsync(
                webhookEvent,
                attempt,
                status,
                ProcessingFailedErrorCode,
                nextRetryAt,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogLeaseLost(webhookEventId);
            dbContext.ChangeTracker.Clear();
        }
    }

    private void LogLeaseLost(Guid webhookEventId) =>
        _logger.LogWarning(
            "Webhook Event {WebhookEventId} was taken over by another worker before its outcome was written; this outcome was discarded.",
            webhookEventId);

    private sealed record AttemptOutcome(
        Guid EndpointId,
        string Result,
        int? HttpStatusCode,
        string? SafeErrorCode,
        DateTimeOffset? NextRetryAt,
        Guid? Id = null);

    private static bool IsRetryable(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests ||
            (int)statusCode == 425 ||
            (int)statusCode >= 500;
    }

    private static bool SupportsEventType(WebhookEndpointRecord endpoint, string eventType)
    {
        if (string.IsNullOrWhiteSpace(endpoint.EventTypes))
        {
            return true;
        }

        return endpoint.EventTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(eventType, StringComparer.Ordinal);
    }

    private static TimeSpan CalculateRetryDelay(
        WebhookDeliveryOptions options,
        int attemptNumber)
    {
        var baseDelay = options.RetryDelay > TimeSpan.Zero
            ? options.RetryDelay
            : TimeSpan.FromMinutes(1);
        var maxDelay = options.MaxRetryDelay > TimeSpan.Zero
            ? options.MaxRetryDelay
            : baseDelay;
        var multiplier = options.RetryBackoffMultiplier >= 1
            ? options.RetryBackoffMultiplier
            : 1;
        var exponent = Math.Max(0, attemptNumber - 1);
        var uncappedTicks = baseDelay.Ticks * Math.Pow(multiplier, exponent);
        var cappedTicks = Math.Min(maxDelay.Ticks, uncappedTicks);
        var delay = TimeSpan.FromTicks(Math.Max(1, (long)cappedTicks));
        var jitterRatio = Math.Clamp(options.RetryJitterRatio, 0, 1);
        if (jitterRatio <= 0)
        {
            return delay;
        }

        var jitterTicks = (long)Math.Round(delay.Ticks * jitterRatio, MidpointRounding.AwayFromZero);
        if (jitterTicks <= 0)
        {
            return delay;
        }

        var offsetTicks = Random.Shared.NextInt64(-jitterTicks, jitterTicks + 1);
        return TimeSpan.FromTicks(Math.Max(1, delay.Ticks + offsetTicks));
    }

    private async Task<WebhookPayload> BuildPayloadAsync(
        WebhookOutboxEventRecord webhookEvent,
        PaymentRecord payment,
        CancellationToken cancellationToken)
    {
        var observedAmounts = await dbContext.MatchingBlockchainTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.ProjectId == payment.ProjectId &&
                transaction.PaymentId == payment.Id)
            .Select(transaction => transaction.ObservedAmount)
            .ToListAsync(cancellationToken);
        var observedTotal = observedAmounts.Count == 0
            ? null
            : observedAmounts
                .Sum(amount => decimal.Parse(amount, CultureInfo.InvariantCulture))
                .ToString("0.############################", CultureInfo.InvariantCulture);
        var expectedCryptoAmountAtomic = payment.SelectedCurrency is null || payment.ExpectedCryptoAmount is null
            ? null
            : PaymentInstructionFactory.ToAtomicAmount(
                payment.SelectedCurrency,
                payment.ExpectedCryptoAmount);

        return new WebhookPayload(
            webhookEvent.Id,
            webhookEvent.EventType,
            webhookEvent.EventVersion,
            webhookEvent.OccurredAt,
            webhookEvent.CorrelationId,
            installationMode?.IsTest ?? false,
            new WebhookResource(webhookEvent.ResourceType, webhookEvent.ResourceId),
            new WebhookPaymentSnapshot(
                payment.Id,
                payment.ExternalReference,
                payment.Status,
                payment.FiatCurrency,
                payment.FiatAmountMinor,
                payment.SelectedCurrency,
                payment.ExpectedCryptoAmount,
                expectedCryptoAmountAtomic,
                observedTotal,
                payment.ConfirmedEligibleTotal,
                PaymentInstructionFactory.GetObservedAmountState(
                    payment.SelectedCurrency,
                    payment.ExpectedCryptoAmount,
                    observedTotal),
                payment.PayerPageId,
                payment.ExpiresAt,
                payment.CompletedAt,
                payment.SettledAt));
    }

    private sealed record WebhookPayload(
        Guid EventId,
        string EventType,
        string EventVersion,
        DateTimeOffset OccurredAt,
        string CorrelationId,
        bool TestMode,
        WebhookResource Resource,
        WebhookPaymentSnapshot Payment);

    private sealed record WebhookResource(
        string Type,
        string Id);

    private sealed record WebhookPaymentSnapshot(
        Guid PaymentId,
        string ExternalReference,
        string Status,
        string FiatCurrency,
        long FiatAmountMinor,
        string? SelectedCurrency,
        string? ExpectedCryptoAmount,
        string? ExpectedCryptoAmountAtomic,
        string? ObservedTotal,
        string? ConfirmedEligibleTotal,
        string? ObservedAmountState,
        string PayerPageId,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? SettledAt);
}

public enum WebhookEventProcessing
{
    /// <summary>No event was due.</summary>
    None,

    /// <summary>One event was claimed and its outcome handled.</summary>
    Processed,

    /// <summary>One event failed for a reason other than the receiver's answer.</summary>
    Failed,
}
