using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminIntegrationApiCredentialStore(PayaffeDbContext dbContext)
    : IAdminIntegrationApiCredentialStore
{
    public async Task<IReadOnlyList<AdminIntegrationApiCredentialReadModel>> ListAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.IntegrationApiCredentials
            .AsNoTracking()
            .OrderBy(credential => credential.Name)
            .ThenBy(credential => credential.Id)
            .Select(credential => new AdminIntegrationApiCredentialReadModel(
                credential.ProjectId,
                credential.Id,
                credential.Name,
                credential.Status,
                credential.CreatedAt,
                credential.LastUsedAt,
                credential.UpdatedAt,
                credential.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminIntegrationApiCredentialReadModel?> CreateAsync(
        AdminIntegrationApiCredentialDraft credential,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var projectAcceptsNewConfiguration = await dbContext.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == credential.ProjectId && project.Status == "active",
                cancellationToken);
        if (!projectAcceptsNewConfiguration)
        {
            return null;
        }

        var record = new IntegrationApiCredentialRecord
        {
            ProjectId = credential.ProjectId,
            Id = credential.Id,
            Name = credential.Name,
            TokenHash = credential.TokenHash,
            Status = "active",
            CreatedAt = credential.CreatedAt,
            UpdatedAt = credential.CreatedAt,
        };
        dbContext.IntegrationApiCredentials.Add(record);
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry, credential.ProjectId));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToReadModel(record);
    }

    public async Task<AdminIntegrationApiCredentialStoreResult> RotateAsync(
        Guid credentialId,
        long expectedVersion,
        string tokenHash,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.IntegrationApiCredentials
            .SingleOrDefaultAsync(candidate => candidate.Id == credentialId, cancellationToken);
        if (credential is null)
        {
            return AdminIntegrationApiCredentialStoreResult.NotFound();
        }

        if (credential.Status == "disabled")
        {
            return AdminIntegrationApiCredentialStoreResult.AlreadyDisabled(ToReadModel(credential));
        }

        if (credential.Version != expectedVersion)
        {
            return AdminIntegrationApiCredentialStoreResult.ConcurrencyConflict(ToReadModel(credential));
        }

        credential.TokenHash = tokenHash;
        credential.UpdatedAt = occurredAt;
        credential.Version++;
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry, credential.ProjectId));

        return await SaveMutationAsync(credentialId, credential, cancellationToken);
    }

    public async Task<AdminIntegrationApiCredentialStoreResult> DisableAsync(
        Guid credentialId,
        long expectedVersion,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.IntegrationApiCredentials
            .SingleOrDefaultAsync(candidate => candidate.Id == credentialId, cancellationToken);
        if (credential is null)
        {
            return AdminIntegrationApiCredentialStoreResult.NotFound();
        }

        if (credential.Status == "disabled")
        {
            return AdminIntegrationApiCredentialStoreResult.AlreadyDisabled(ToReadModel(credential));
        }

        if (credential.Version != expectedVersion)
        {
            return AdminIntegrationApiCredentialStoreResult.ConcurrencyConflict(ToReadModel(credential));
        }

        credential.Status = "disabled";
        credential.UpdatedAt = occurredAt;
        credential.Version++;
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry, credential.ProjectId));

        return await SaveMutationAsync(credentialId, credential, cancellationToken);
    }

    private async Task<AdminIntegrationApiCredentialStoreResult> SaveMutationAsync(
        Guid credentialId,
        IntegrationApiCredentialRecord credential,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return AdminIntegrationApiCredentialStoreResult.Updated(ToReadModel(credential));
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.IntegrationApiCredentials
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == credentialId, cancellationToken);
            return current is null
                ? AdminIntegrationApiCredentialStoreResult.NotFound()
                : AdminIntegrationApiCredentialStoreResult.ConcurrencyConflict(ToReadModel(current));
        }
    }

    private static AdminIntegrationApiCredentialReadModel ToReadModel(
        IntegrationApiCredentialRecord credential) =>
        new(
            credential.ProjectId,
            credential.Id,
            credential.Name,
            credential.Status,
            credential.CreatedAt,
            credential.LastUsedAt,
            credential.UpdatedAt,
            credential.Version);

    private static AuditLogEntryRecord ToAuditRecord(
        AdminAuditEntry auditEntry,
        Guid projectId) =>
        new()
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
        };
}
