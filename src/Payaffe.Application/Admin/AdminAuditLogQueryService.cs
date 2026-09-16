namespace Payaffe.Application.Admin;

public sealed class AdminAuditLogQueryService(IAdminAuditLogStore store)
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public async Task<IReadOnlyList<AdminAuditLogEntryReadModel>> ListRecentEntriesAndRecordAccessAsync(
        Guid? projectId,
        int? requestedLimit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(requestedLimit ?? DefaultLimit, 1, MaxLimit);
        return await store.ListRecentEntriesAndRecordAccessAsync(projectId, limit, accessAuditEntry, cancellationToken);
    }

    public async Task<AdminAuditLogEntryDetailReadModel?> FindEntryAndRecordAccessAsync(
        Guid? projectId,
        Guid eventId,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty)
        {
            return null;
        }

        return await store.FindEntryAndRecordAccessAsync(projectId, eventId, accessAuditEntry, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAuditLogEntryDetailReadModel>> ExportRecentEntriesAndRecordAccessAsync(
        Guid? projectId,
        int? requestedLimit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(requestedLimit ?? DefaultLimit, 1, MaxLimit);
        return await store.ExportRecentEntriesAndRecordAccessAsync(projectId, limit, accessAuditEntry, cancellationToken);
    }

    public async Task RecordAccessAsync(
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        await store.RecordAccessAsync(accessAuditEntry, cancellationToken);
    }
}
