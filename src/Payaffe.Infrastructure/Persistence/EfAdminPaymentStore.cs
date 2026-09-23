using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminPaymentStore(PayaffeDbContext dbContext) : IAdminPaymentStore
{
    public async Task<IReadOnlyList<AdminPaymentSummaryReadModel>> ListRecentPaymentsAsync(
        Guid projectId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.ProjectId == projectId)
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenBy(payment => payment.Id)
            .Take(limit)
            .Select(payment => new AdminPaymentSummaryReadModel(
                payment.ProjectId,
                payment.Id,
                payment.ExternalReference,
                payment.FiatCurrency,
                payment.FiatAmountMinor,
                payment.Status,
                payment.SelectedCurrency,
                payment.ExpectedCryptoAmount,
                payment.PaymentAddress,
                payment.ExpiresAt,
                payment.CompletedAt,
                payment.CreatedAt,
                payment.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminPaymentDetailReadModel?> FindPaymentAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ProjectId == projectId && candidate.Id == paymentId,
                cancellationToken);
        if (payment is null)
        {
            return null;
        }

        return await ToDetailAsync(payment, cancellationToken);
    }

    public async Task<AdminPaymentSettlementResult> SettleAsync(
        Guid projectId,
        Guid paymentId,
        long expectedVersion,
        string reason,
        DateTimeOffset settledAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var payment = await dbContext.Payments.SingleOrDefaultAsync(
            candidate => candidate.ProjectId == projectId && candidate.Id == paymentId,
            cancellationToken);
        if (payment is null)
        {
            return AdminPaymentSettlementResult.NotFound();
        }

        if (payment.Version != expectedVersion)
        {
            return AdminPaymentSettlementResult.ConcurrencyConflict(
                await ToDetailAsync(payment, cancellationToken));
        }

        var hasObservation = await dbContext.MatchingBlockchainTransactions
            .AnyAsync(
                blockchainTransaction => blockchainTransaction.ProjectId == projectId &&
                                         blockchainTransaction.PaymentId == paymentId,
                cancellationToken);
        if (payment.Status is not ("observed" or "expired") || !hasObservation)
        {
            return AdminPaymentSettlementResult.NotSettleable(
                await ToDetailAsync(payment, cancellationToken));
        }

        payment.Status = "settled";
        payment.SettledAt = settledAt;
        payment.UpdatedAt = settledAt;
        payment.Version++;
        dbContext.PaymentEventHistory.Add(new PaymentEventHistoryRecord
        {
            ProjectId = projectId,
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            EventType = "payment.settled",
            OccurredAt = settledAt,
            Details = JsonSerializer.Serialize(new { reason }),
        });
        dbContext.WebhookOutboxEvents.Add(new WebhookOutboxEventRecord
        {
            ProjectId = projectId,
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            IntegrationApiCredentialId = payment.IntegrationApiCredentialId,
            EventType = "payment.settled",
            EventVersion = "1",
            PayloadVersion = 1,
            ResourceType = "payment",
            ResourceId = payment.Id.ToString("D"),
            Status = "pending",
            OccurredAt = settledAt,
            CreatedAt = settledAt,
            NextAttemptAt = settledAt,
            AttemptCount = 0,
            CorrelationId = auditEntry.CorrelationId,
        });
        dbContext.AuditLogEntries.Add(new AuditLogEntryRecord
        {
            ProjectId = projectId,
            EventId = auditEntry.EventId,
            OccurredAt = auditEntry.OccurredAt,
            EventType = auditEntry.EventType,
            Outcome = auditEntry.Outcome,
            ActorType = auditEntry.ActorType,
            ActorId = auditEntry.ActorId,
            SourceService = auditEntry.SourceService,
            SourceIp = auditEntry.SourceIp,
            UserAgent = auditEntry.UserAgent,
            CorrelationId = auditEntry.CorrelationId,
            ReasonCode = auditEntry.ReasonCode,
            SubjectType = auditEntry.SubjectType,
            SubjectId = auditEntry.SubjectId,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return AdminPaymentSettlementResult.Settled(
            await ToDetailAsync(payment, cancellationToken));
    }

    public async Task<IReadOnlyList<AdminReorgAlertReadModel>> ListReorgAlertsAsync(
        Guid projectId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await dbContext.ReorgAlerts
            .AsNoTracking()
            .Where(alert => alert.ProjectId == projectId)
            .OrderByDescending(alert => alert.CreatedAt)
            .ThenBy(alert => alert.Id)
            .Take(limit)
            .Select(alert => new AdminReorgAlertReadModel(
                alert.ProjectId,
                alert.Id,
                alert.PaymentId,
                alert.SupportedCurrency,
                alert.TransactionHash,
                alert.PreviousConfirmations,
                alert.NewConfirmations,
                alert.PreviousBlockHash,
                alert.NewBlockHash,
                alert.PreviousBlockHeight,
                alert.NewBlockHeight,
                alert.Status,
                alert.CreatedAt,
                alert.UpdatedAt,
                alert.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAddressHistoryAlertReadModel>> ListAddressHistoryAlertsAsync(
        Guid projectId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await dbContext.AddressHistoryAlerts
            .AsNoTracking()
            .Where(alert => alert.ProjectId == projectId)
            .OrderByDescending(alert => alert.CreatedAt)
            .ThenBy(alert => alert.Id)
            .Take(limit)
            .Select(alert => new AdminAddressHistoryAlertReadModel(
                alert.ProjectId,
                alert.Id,
                alert.PaymentId,
                alert.SupportedCurrency,
                alert.PaymentAddress,
                alert.TransactionHash,
                alert.ObservedAmount,
                alert.ObservedAt,
                alert.CurrencySelectedAt,
                alert.Status,
                alert.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<AdminPaymentDetailReadModel> ToDetailAsync(
        Records.PaymentRecord payment,
        CancellationToken cancellationToken)
    {
        var observedTotal = await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken);
        return new AdminPaymentDetailReadModel(
            payment.ProjectId,
            payment.Id,
            payment.ExternalReference,
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            payment.Status,
            payment.PayerPageId,
            payment.ExpiresAt,
            payment.LateAcceptanceEndsAt,
            payment.SelectedCurrency,
            payment.ExpectedCryptoAmount,
            payment.PaymentAddress,
            observedTotal,
            payment.ConfirmedEligibleTotal,
            payment.CompletedAt,
            payment.CreatedAt,
            payment.UpdatedAt,
            payment.SettledAt,
            payment.Version);
    }

    private async Task<string?> CalculateObservedTotalAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var observedAmounts = await dbContext.MatchingBlockchainTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ProjectId == projectId && transaction.PaymentId == paymentId)
            .Select(transaction => transaction.ObservedAmount)
            .ToListAsync(cancellationToken);
        if (observedAmounts.Count == 0)
        {
            return null;
        }

        var total = observedAmounts.Sum(static amount =>
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
        return total.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
    }
}
