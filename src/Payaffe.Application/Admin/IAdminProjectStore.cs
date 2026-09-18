namespace Payaffe.Application.Admin;

public interface IAdminProjectStore
{
    Task<IReadOnlyList<AdminProjectReadModel>> ListAsync(CancellationToken cancellationToken);

    Task<AdminProjectResult> CreateAsync(
        AdminProjectDraft project,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<AdminProjectResult> ChangeStatusAsync(
        Guid projectId,
        long expectedVersion,
        string status,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);
}
