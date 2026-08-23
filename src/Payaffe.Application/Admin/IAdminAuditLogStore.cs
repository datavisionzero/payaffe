namespace Payaffe.Application.Admin;

public interface IAdminAuditLogStore
{
    Task<IReadOnlyList<AdminAuditLogEntryReadModel>> ListRecentEntriesAndRecordAccessAsync(
        int limit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);

    Task<AdminAuditLogEntryDetailReadModel?> FindEntryAndRecordAccessAsync(
        Guid eventId,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminAuditLogEntryDetailReadModel>> ExportRecentEntriesAndRecordAccessAsync(
        int limit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);

    Task RecordAccessAsync(
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);
}
