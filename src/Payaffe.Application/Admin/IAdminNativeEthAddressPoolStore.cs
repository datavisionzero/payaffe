namespace Payaffe.Application.Admin;

public interface IAdminNativeEthAddressPoolStore
{
    Task<AdminNativeEthAddressPoolSummary> GetSummaryAsync(
        int lowCapacityThreshold,
        CancellationToken cancellationToken);

    Task<AdminNativeEthAddressPoolImportResult> ImportAsync(
        AdminNativeEthAddressPoolImportDraft import,
        int lowCapacityThreshold,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);
}
