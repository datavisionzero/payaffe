using Payaffe.Application.Admin;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminWebhookDeliveryStore(PayaffeDbContext dbContext) : IAdminWebhookDeliveryStore
{
    public async Task<IReadOnlyList<AdminWebhookDeliveryReadModel>> ListResendableDeliveriesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var events = await dbContext.WebhookOutboxEvents
            .AsNoTracking()
            .Where(webhookEvent => webhookEvent.Status == "retry_pending" || webhookEvent.Status == "terminal_failed")
            .Join(
                dbContext.Payments.AsNoTracking(),
                webhookEvent => new { webhookEvent.ProjectId, Id = webhookEvent.PaymentId },
                payment => new { payment.ProjectId, payment.Id },
                (webhookEvent, payment) => new
                {
                    WebhookEvent = webhookEvent,
                    PaymentExternalReference = payment.ExternalReference,
                })
            .OrderByDescending(candidate => candidate.WebhookEvent.CreatedAt)
            .ThenBy(candidate => candidate.WebhookEvent.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var eventIds = events.Select(candidate => candidate.WebhookEvent.Id).ToArray();
        var attemptRows = await dbContext.WebhookDeliveryAttempts
            .AsNoTracking()
            .Where(attempt => eventIds.Contains(attempt.WebhookEventId))
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .ThenByDescending(attempt => attempt.AttemptedAt)
            .ToListAsync(cancellationToken);
        var attempts = attemptRows
            .GroupBy(attempt => attempt.WebhookEventId)
            .ToDictionary(group => group.Key, group => group.First());

        return events
            .Select(candidate =>
            {
                attempts.TryGetValue(candidate.WebhookEvent.Id, out var lastAttempt);
                return new AdminWebhookDeliveryReadModel(
                    candidate.WebhookEvent.ProjectId,
                    candidate.WebhookEvent.Id,
                    candidate.WebhookEvent.PaymentId,
                    candidate.PaymentExternalReference,
                    candidate.WebhookEvent.EventType,
                    candidate.WebhookEvent.EventVersion,
                    candidate.WebhookEvent.Status,
                    candidate.WebhookEvent.AttemptCount,
                    candidate.WebhookEvent.LastErrorCode,
                    candidate.WebhookEvent.Status == "retry_pending"
                        ? candidate.WebhookEvent.NextAttemptAt
                        : null,
                    candidate.WebhookEvent.OccurredAt,
                    candidate.WebhookEvent.CreatedAt,
                    lastAttempt?.AttemptedAt,
                    lastAttempt?.Result,
                    lastAttempt?.HttpStatusCode,
                    lastAttempt?.SafeErrorCode,
                    candidate.WebhookEvent.CorrelationId);
            })
            .ToArray();
    }
}
