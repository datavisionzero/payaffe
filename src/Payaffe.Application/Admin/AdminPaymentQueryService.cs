using Payaffe.Application.Payments;

namespace Payaffe.Application.Admin;

public sealed class AdminPaymentQueryService(IAdminPaymentStore store, IClock clock)
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    public async Task<IReadOnlyList<AdminPaymentSummaryReadModel>> ListRecentPaymentsAsync(
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var limit = requestedLimit is null or <= 0
            ? DefaultLimit
            : Math.Min(requestedLimit.Value, MaxLimit);

        return await store.ListRecentPaymentsAsync(limit, cancellationToken);
    }

    public async Task<AdminPaymentDetailReadModel?> FindPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId == Guid.Empty)
        {
            return null;
        }

        return await store.FindPaymentAsync(paymentId, cancellationToken);
    }

    public Task<AdminPaymentSettlementResult> SettleAsync(
        Guid paymentId,
        long expectedVersion,
        string? reason,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        var normalizedReason = reason?.Trim();
        if (paymentId == Guid.Empty ||
            expectedVersion <= 0 ||
            string.IsNullOrWhiteSpace(normalizedReason) ||
            normalizedReason.Length > 500)
        {
            return Task.FromResult(AdminPaymentSettlementResult.InvalidInput());
        }

        var settledAt = clock.UtcNow;
        return store.SettleAsync(
            paymentId,
            expectedVersion,
            normalizedReason,
            settledAt,
            new AdminAuditEntry(
                Guid.NewGuid(),
                settledAt,
                "admin.payment.settle",
                "success",
                "product_user",
                context.AdminAccountId.ToString("D"),
                context.SourceService,
                context.SourceIp,
                context.UserAgent,
                context.CorrelationId,
                "payment.settled",
                "payment",
                paymentId.ToString("D")),
            cancellationToken);
    }

    public Task<IReadOnlyList<AdminReorgAlertReadModel>> ListReorgAlertsAsync(
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var limit = requestedLimit is null or <= 0
            ? DefaultLimit
            : Math.Min(requestedLimit.Value, MaxLimit);
        return store.ListReorgAlertsAsync(limit, cancellationToken);
    }
}
