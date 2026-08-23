namespace Payaffe.Application.Admin;

public interface IAdminPaymentStore
{
    Task<IReadOnlyList<AdminPaymentSummaryReadModel>> ListRecentPaymentsAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<AdminPaymentDetailReadModel?> FindPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken);

    Task<AdminPaymentSettlementResult> SettleAsync(
        Guid paymentId,
        long expectedVersion,
        string reason,
        DateTimeOffset settledAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminReorgAlertReadModel>> ListReorgAlertsAsync(
        int limit,
        CancellationToken cancellationToken);
}
