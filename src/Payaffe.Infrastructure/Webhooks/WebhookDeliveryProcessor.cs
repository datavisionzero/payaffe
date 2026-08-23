using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Webhooks;

public sealed class WebhookDeliveryProcessor(
    PayaffeDbContext dbContext,
    HttpClient httpClient,
    IWebhookSecretResolver secretResolver,
    IClock clock,
    IOptions<WebhookDeliveryOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WebhookDeliveryOptions _options = options.Value;

    private readonly string _workerId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var webhookEvent = await ClaimNextAsync(now, cancellationToken);

        if (webhookEvent is null)
        {
            return false;
        }

        try
        {
            await DeliverAsync(webhookEvent, cancellationToken);
        }
        finally
        {
            webhookEvent.LockedBy = null;
            webhookEvent.LockedUntil = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Claims exactly one due Webhook Event so that concurrent workers cannot
    /// deliver the same event twice. An active lease is skipped and becomes
    /// claimable again once it expires, which recovers a crashed worker's event.
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

    public async Task<WebhookManualResendResult> ResendAsync(
        Guid webhookEventId,
        CancellationToken cancellationToken)
    {
        var webhookEvent = await dbContext.WebhookOutboxEvents
            .SingleOrDefaultAsync(candidate => candidate.Id == webhookEventId, cancellationToken);
        if (webhookEvent is null)
        {
            return WebhookManualResendResult.NotFound();
        }

        if (webhookEvent.Status is not ("retry_pending" or "terminal_failed"))
        {
            return WebhookManualResendResult.NotResendable(webhookEvent.Status);
        }

        await DeliverAsync(webhookEvent, cancellationToken);
        return WebhookManualResendResult.Resent(webhookEvent.Status);
    }

    private async Task DeliverAsync(
        WebhookOutboxEventRecord webhookEvent,
        CancellationToken cancellationToken)
    {
        var endpoints = await dbContext.WebhookEndpoints
            .Where(candidate =>
                candidate.IntegrationApiCredentialId == webhookEvent.IntegrationApiCredentialId &&
                candidate.Status == "active")
            .OrderBy(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);
        var endpoint = endpoints.FirstOrDefault(candidate => SupportsEventType(candidate, webhookEvent.EventType));
        if (endpoint is null)
        {
            webhookEvent.Status = "terminal_failed";
            webhookEvent.LastErrorCode = "webhook_endpoint.not_found";
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var payment = await dbContext.Payments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == webhookEvent.PaymentId, cancellationToken);
        var secret = await secretResolver.ResolveAsync(endpoint.SecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(secret))
        {
            await RecordAttemptAsync(
                webhookEvent,
                endpoint,
                "terminal_failed",
                httpStatusCode: null,
                "webhook_secret.unavailable",
                nextRetryAt: null,
                cancellationToken);
            webhookEvent.Status = "terminal_failed";
            webhookEvent.LastErrorCode = "webhook_secret.unavailable";
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var payload = BuildPayload(webhookEvent, payment);
        var rawBody = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(rawBody, Encoding.UTF8, "application/json"),
        };
        var deliveryId = Guid.NewGuid();
        var timestamp = clock.UtcNow;
        request.Headers.Add("Payaffe-Webhook-Id", deliveryId.ToString("D"));
        request.Headers.Add("Payaffe-Webhook-Timestamp", timestamp.ToUnixTimeSeconds().ToString());
        request.Headers.Add("Payaffe-Webhook-Signature", WebhookSignatureService.CreateSignature(secret, timestamp, rawBody));
        request.Headers.Add("Payaffe-Webhook-Event-Type", webhookEvent.EventType);
        request.Headers.Add("Payaffe-Webhook-Event-Version", webhookEvent.EventVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            await ApplyHttpResultAsync(webhookEvent, endpoint, response.StatusCode, cancellationToken);
        }
        catch (HttpRequestException)
        {
            await ApplyRetryableFailureAsync(webhookEvent, endpoint, "http.request_failed", cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await ApplyRetryableFailureAsync(webhookEvent, endpoint, "http.timeout", cancellationToken);
        }
    }

    private async Task ApplyHttpResultAsync(
        WebhookOutboxEventRecord webhookEvent,
        WebhookEndpointRecord endpoint,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        if ((int)statusCode >= 200 && (int)statusCode <= 299)
        {
            var nextAttemptNumber = webhookEvent.AttemptCount + 1;
            await RecordAttemptAsync(
                webhookEvent,
                endpoint,
                "succeeded",
                (int)statusCode,
                safeErrorCode: null,
                nextRetryAt: null,
                cancellationToken);
            webhookEvent.AttemptCount = nextAttemptNumber;
            webhookEvent.Status = "delivered";
            webhookEvent.LastErrorCode = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (IsRetryable(statusCode))
        {
            await ApplyRetryableFailureAsync(webhookEvent, endpoint, $"http.{(int)statusCode}", cancellationToken, (int)statusCode);
            return;
        }

        await RecordAttemptAsync(
            webhookEvent,
            endpoint,
            "terminal_failed",
            (int)statusCode,
            $"http.{(int)statusCode}",
            nextRetryAt: null,
            cancellationToken);
        webhookEvent.AttemptCount++;
        webhookEvent.Status = "terminal_failed";
        webhookEvent.LastErrorCode = $"http.{(int)statusCode}";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyRetryableFailureAsync(
        WebhookOutboxEventRecord webhookEvent,
        WebhookEndpointRecord endpoint,
        string safeErrorCode,
        CancellationToken cancellationToken,
        int? httpStatusCode = null)
    {
        var nextAttemptNumber = webhookEvent.AttemptCount + 1;
        var exhausted = nextAttemptNumber >= _options.MaxAttempts;
        DateTimeOffset? nextRetryAt = exhausted
            ? null
            : clock.UtcNow.Add(CalculateRetryDelay(_options, nextAttemptNumber));
        await RecordAttemptAsync(
            webhookEvent,
            endpoint,
            exhausted ? "terminal_failed" : "retry_pending",
            httpStatusCode,
            safeErrorCode,
            nextRetryAt,
            cancellationToken);

        webhookEvent.AttemptCount = nextAttemptNumber;
        webhookEvent.Status = exhausted ? "terminal_failed" : "retry_pending";
        webhookEvent.NextAttemptAt = nextRetryAt ?? webhookEvent.NextAttemptAt;
        webhookEvent.LastErrorCode = safeErrorCode;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordAttemptAsync(
        WebhookOutboxEventRecord webhookEvent,
        WebhookEndpointRecord endpoint,
        string result,
        int? httpStatusCode,
        string? safeErrorCode,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken)
    {
        dbContext.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttemptRecord
        {
            Id = Guid.NewGuid(),
            WebhookEventId = webhookEvent.Id,
            WebhookEndpointId = endpoint.Id,
            AttemptNumber = webhookEvent.AttemptCount + 1,
            AttemptedAt = clock.UtcNow,
            Result = result,
            HttpStatusCode = httpStatusCode,
            SafeErrorCode = safeErrorCode,
            NextRetryAt = nextRetryAt,
            CorrelationId = webhookEvent.CorrelationId,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        // Every attempt outcome passes through here, including manual resend,
        // so this is the one place the delivery counter has to be updated.
        PayaffeTelemetry.RecordWebhookAttempt(result);
    }

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

    private static WebhookPayload BuildPayload(
        WebhookOutboxEventRecord webhookEvent,
        PaymentRecord payment)
    {
        return new WebhookPayload(
            webhookEvent.Id,
            webhookEvent.EventType,
            webhookEvent.EventVersion,
            webhookEvent.OccurredAt,
            webhookEvent.CorrelationId,
            new WebhookResource(webhookEvent.ResourceType, webhookEvent.ResourceId),
            new WebhookPaymentSnapshot(
                payment.Id,
                payment.ExternalReference,
                payment.Status,
                payment.FiatCurrency,
                payment.FiatAmountMinor,
                payment.SelectedCurrency,
                payment.ExpectedCryptoAmount,
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
        string PayerPageId,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? SettledAt);
}
