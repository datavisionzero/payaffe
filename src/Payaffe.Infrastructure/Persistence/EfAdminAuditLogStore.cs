using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminAuditLogStore(PayaffeDbContext dbContext) : IAdminAuditLogStore
{
    public async Task<IReadOnlyList<AdminAuditLogEntryReadModel>> ListRecentEntriesAndRecordAccessAsync(
        int limit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        var entries = await dbContext.AuditLogEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.EventId)
            .Take(limit)
            .Select(entry => new AdminAuditLogEntryReadModel(
                entry.EventId,
                entry.OccurredAt,
                entry.EventType,
                entry.Outcome,
                entry.ActorType,
                entry.ActorId,
                entry.ReasonCode,
                entry.SubjectType,
                entry.SubjectId))
            .ToListAsync(cancellationToken);

        AddAuditEntry(accessAuditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entries;
    }

    public async Task<AdminAuditLogEntryDetailReadModel?> FindEntryAndRecordAccessAsync(
        Guid eventId,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.AuditLogEntries
            .AsNoTracking()
            .Where(candidate => candidate.EventId == eventId)
            .Select(candidate => new AdminAuditLogEntryDetailReadModel(
                candidate.EventId,
                candidate.OccurredAt,
                candidate.EventType,
                candidate.Outcome,
                candidate.ActorType,
                candidate.ActorId,
                candidate.SourceService,
                candidate.SourceIp,
                candidate.UserAgent,
                candidate.CorrelationId,
                candidate.ReasonCode,
                candidate.SubjectType,
                candidate.SubjectId))
            .SingleOrDefaultAsync(cancellationToken);

        if (entry is not null)
        {
            AddAuditEntry(accessAuditEntry);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return entry;
    }

    public async Task<IReadOnlyList<AdminAuditLogEntryDetailReadModel>> ExportRecentEntriesAndRecordAccessAsync(
        int limit,
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        var entries = await dbContext.AuditLogEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.EventId)
            .Take(limit)
            .Select(entry => new AdminAuditLogEntryDetailReadModel(
                entry.EventId,
                entry.OccurredAt,
                entry.EventType,
                entry.Outcome,
                entry.ActorType,
                entry.ActorId,
                entry.SourceService,
                entry.SourceIp,
                entry.UserAgent,
                entry.CorrelationId,
                entry.ReasonCode,
                entry.SubjectType,
                entry.SubjectId))
            .ToListAsync(cancellationToken);

        AddAuditEntry(accessAuditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entries;
    }

    public async Task RecordAccessAsync(
        AdminAuditEntry accessAuditEntry,
        CancellationToken cancellationToken)
    {
        AddAuditEntry(accessAuditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddAuditEntry(AdminAuditEntry accessAuditEntry)
    {
        dbContext.AuditLogEntries.Add(new AuditLogEntryRecord
        {
            EventId = accessAuditEntry.EventId,
            OccurredAt = accessAuditEntry.OccurredAt,
            EventType = accessAuditEntry.EventType,
            Outcome = accessAuditEntry.Outcome,
            ActorType = accessAuditEntry.ActorType,
            ActorId = accessAuditEntry.ActorId,
            SourceService = accessAuditEntry.SourceService,
            SourceIp = accessAuditEntry.SourceIp,
            UserAgent = accessAuditEntry.UserAgent,
            CorrelationId = accessAuditEntry.CorrelationId,
            ReasonCode = accessAuditEntry.ReasonCode,
            SubjectType = accessAuditEntry.SubjectType,
            SubjectId = accessAuditEntry.SubjectId,
        });
    }
}
