namespace Payaffe.Application.Admin;

public interface IAdminAuditLogStore
{
    Task<IReadOnlyList<AdminAuditLogEntryReadModel>> ListRecentEntriesAndRecordAccessAsync(
        Guid? projectId,
        int limit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);

    Task<AdminAuditLogEntryDetailReadModel?> FindEntryAndRecordAccessAsync(
        Guid? projectId,
        Guid eventId,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminAuditLogEntryDetailReadModel>> ExportRecentEntriesAndRecordAccessAsync(
        Guid? projectId,
        int limit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);

    Task RecordAccessAsync(
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken);
}
