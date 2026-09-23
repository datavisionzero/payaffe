namespace Payaffe.Application.Admin;

public interface IAdminPaymentStore
{
    Task<IReadOnlyList<AdminPaymentSummaryReadModel>> ListRecentPaymentsAsync(
        Guid projectId,
        int limit,
        CancellationToken cancellationToken);

    Task<AdminPaymentDetailReadModel?> FindPaymentAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken);

    Task<AdminPaymentSettlementResult> SettleAsync(
        Guid projectId,
        Guid paymentId,
        long expectedVersion,
        string reason,
        DateTimeOffset settledAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminReorgAlertReadModel>> ListReorgAlertsAsync(
        Guid projectId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminAddressHistoryAlertReadModel>> ListAddressHistoryAlertsAsync(
        Guid projectId,
        int limit,
        CancellationToken cancellationToken);
}
