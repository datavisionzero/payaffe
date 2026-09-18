namespace Payaffe.Application.Admin;

public interface IAdminNativeEthAddressPoolStore
{
    Task<AdminNativeEthAddressPoolSummary> GetSummaryAsync(
        Guid projectId,
        int lowCapacityThreshold,
        CancellationToken cancellationToken);

    Task<AdminNativeEthAddressPoolImportResult> ImportAsync(
        Guid projectId,
        AdminNativeEthAddressPoolImportDraft import,
        int lowCapacityThreshold,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);
}
