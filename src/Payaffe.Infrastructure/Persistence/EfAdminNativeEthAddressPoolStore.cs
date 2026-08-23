using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminNativeEthAddressPoolStore(PayaffeDbContext dbContext)
    : IAdminNativeEthAddressPoolStore
{
    public async Task<AdminNativeEthAddressPoolSummary> GetSummaryAsync(
        int lowCapacityThreshold,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.NativeEthAddresses
            .AsNoTracking()
            .GroupBy(address => address.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var unused = counts.GetValueOrDefault("unused");
        return new AdminNativeEthAddressPoolSummary(
            unused,
            counts.GetValueOrDefault("assigned"),
            counts.GetValueOrDefault("retired"),
            lowCapacityThreshold,
            unused <= lowCapacityThreshold);
    }

    public async Task<AdminNativeEthAddressPoolImportResult> ImportAsync(
        AdminNativeEthAddressPoolImportDraft import,
        int lowCapacityThreshold,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var duplicateExists = await dbContext.NativeEthAddresses
            .AsNoTracking()
            .AnyAsync(address => import.Addresses.Contains(address.Address), cancellationToken);
        if (duplicateExists)
        {
            return AdminNativeEthAddressPoolImportResult.DuplicateAddress();
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        dbContext.NativeEthAddressPoolImports.Add(new NativeEthAddressPoolImportRecord
        {
            Id = import.ImportId,
            ImportedByAdminAccountId = import.ImportedByAdminAccountId,
            AddressCount = import.Addresses.Count,
            ImportedAt = import.ImportedAt,
        });
        dbContext.NativeEthAddresses.AddRange(import.Addresses.Select(address =>
            new NativeEthAddressRecord
            {
                Id = Guid.NewGuid(),
                ImportId = import.ImportId,
                Address = address,
                Status = "unused",
                CreatedAt = import.ImportedAt,
                UpdatedAt = import.ImportedAt,
                Version = 1,
            }));
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  {
                      SqlState: PostgresErrorCodes.UniqueViolation,
                  })
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return AdminNativeEthAddressPoolImportResult.DuplicateAddress();
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var summary = await GetSummaryAsync(lowCapacityThreshold, cancellationToken);
        return AdminNativeEthAddressPoolImportResult.Success(
            import.ImportId,
            import.Addresses.Count,
            summary);
    }

    private static AuditLogEntryRecord ToAuditRecord(AdminAuditEntry auditEntry) =>
        new()
        {
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
