using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminProjectStore(PayaffeDbContext dbContext) : IAdminProjectStore
{
    public async Task<IReadOnlyList<AdminProjectReadModel>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Projects
            .AsNoTracking()
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Select(project => ToReadModel(project))
            .ToListAsync(cancellationToken);

    public async Task<AdminProjectResult> CreateAsync(
        AdminProjectDraft project,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var record = new ProjectRecord
        {
            Id = project.ProjectId,
            Name = project.Name,
            Slug = project.Slug,
            Status = "active",
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.CreatedAt,
        };
        dbContext.Projects.Add(record);
        dbContext.ProjectConfigurations.Add(new ProjectConfigurationRecord
        {
            ProjectId = project.ProjectId,
            PaymentExpirationSeconds = project.PaymentExpirationSeconds,
            LateAcceptanceWindowSeconds = project.LateAcceptanceWindowSeconds,
            PaymentTolerancePercent = project.PaymentTolerancePercent,
            BtcEnabled = true,
            LtcEnabled = true,
            EthEnabled = true,
            BtcConfirmationRequirement = project.BtcConfirmationRequirement,
            LtcConfirmationRequirement = project.LtcConfirmationRequirement,
            EthConfirmationRequirement = project.EthConfirmationRequirement,
            BtcReorgMonitoringDepth = project.BtcReorgMonitoringDepth,
            LtcReorgMonitoringDepth = project.LtcReorgMonitoringDepth,
            EthReorgMonitoringDepth = project.EthReorgMonitoringDepth,
            NativeEthLowCapacityThreshold = project.NativeEthLowCapacityThreshold,
            LegacySettingsFingerprint = string.Empty,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.CreatedAt,
        });
        dbContext.AuditLogEntries.Add(ToAuditRecord(project.ProjectId, auditEntry));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return AdminProjectResult.Updated(ToReadModel(record));
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return AdminProjectResult.SlugConflict();
        }
    }

    public async Task<AdminProjectResult> ChangeStatusAsync(
        Guid projectId,
        long expectedVersion,
        string status,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.SingleOrDefaultAsync(
            candidate => candidate.Id == projectId,
            cancellationToken);
        if (project is null)
        {
            return AdminProjectResult.NotFound();
        }

        if (project.Version != expectedVersion)
        {
            return AdminProjectResult.ConcurrencyConflict(ToReadModel(project));
        }

        var validTransition = (project.Status, status) switch
        {
            ("active", "disabled") => true,
            ("disabled", "active") => true,
            ("disabled", "archived") => true,
            ("archived", "disabled") => true,
            _ when project.Status == status => true,
            _ => false,
        };
        if (!validTransition)
        {
            return AdminProjectResult.InvalidTransition(ToReadModel(project));
        }

        if (status == "archived" && await HasActiveWorkAsync(projectId, cancellationToken))
        {
            return AdminProjectResult.HasActiveWork(ToReadModel(project));
        }

        project.Status = status;
        project.UpdatedAt = occurredAt;
        project.Version++;
        dbContext.AuditLogEntries.Add(ToAuditRecord(projectId, auditEntry));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return AdminProjectResult.Updated(ToReadModel(project));
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.Projects.AsNoTracking().SingleAsync(
                candidate => candidate.Id == projectId,
                cancellationToken);
            return AdminProjectResult.ConcurrencyConflict(ToReadModel(current));
        }
    }

    private async Task<bool> HasActiveWorkAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (await dbContext.Payments.AnyAsync(
                payment => payment.ProjectId == projectId &&
                           payment.Status != "completed" &&
                           payment.Status != "expired" &&
                           payment.Status != "settled",
                cancellationToken))
        {
            return true;
        }

        if (await dbContext.WebhookOutboxEvents.AnyAsync(
                webhookEvent => webhookEvent.ProjectId == projectId &&
                                (webhookEvent.Status == "pending" || webhookEvent.Status == "retry_pending"),
                cancellationToken))
        {
            return true;
        }

        return await (
                from transaction in dbContext.MatchingBlockchainTransactions
                join payment in dbContext.Payments
                    on new { transaction.ProjectId, Id = transaction.PaymentId }
                    equals new { payment.ProjectId, payment.Id }
                where payment.ProjectId == projectId &&
                      payment.Status == "completed" &&
                      transaction.ContributedToCompletion &&
                      !transaction.ReorgAffected &&
                      transaction.Confirmations <
                      (payment.ConfirmationRequirement ?? 0) + (payment.ReorgMonitoringDepth ?? 0)
                select transaction.Id)
            .AnyAsync(cancellationToken);
    }

    private static AdminProjectReadModel ToReadModel(ProjectRecord project) => new(
        project.Id, project.Name, project.Slug, project.Status,
        project.CreatedAt, project.UpdatedAt, project.Version);

    private static AuditLogEntryRecord ToAuditRecord(Guid projectId, AdminAuditEntry auditEntry) => new()
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
