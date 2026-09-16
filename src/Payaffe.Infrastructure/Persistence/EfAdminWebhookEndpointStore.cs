using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminWebhookEndpointStore(PayaffeDbContext dbContext)
    : IAdminWebhookEndpointStore
{
    public async Task<IReadOnlyList<AdminWebhookEndpointReadModel>> ListAsync(
        Guid? integrationApiCredentialId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.WebhookEndpoints.AsNoTracking();
        if (integrationApiCredentialId is not null)
        {
            query = query.Where(endpoint =>
                endpoint.IntegrationApiCredentialId == integrationApiCredentialId.Value);
        }

        var endpoints = await query
            .OrderBy(endpoint => endpoint.CreatedAt)
            .ThenBy(endpoint => endpoint.Id)
            .ToListAsync(cancellationToken);
        return endpoints.Select(ToReadModel).ToArray();
    }

    public async Task<AdminWebhookEndpointStoreResult> CreateAsync(
        AdminWebhookEndpointDraft endpoint,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var projectId = await dbContext.IntegrationApiCredentials
            .Where(
                credential =>
                    credential.Id == endpoint.IntegrationApiCredentialId &&
                    credential.Status == "active")
            .Select(credential => (Guid?)credential.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (projectId is null)
        {
            return AdminWebhookEndpointStoreResult.ParentCredentialUnavailable();
        }

        var record = new WebhookEndpointRecord
        {
            ProjectId = projectId.Value,
            Id = endpoint.Id,
            IntegrationApiCredentialId = endpoint.IntegrationApiCredentialId,
            Url = endpoint.Url,
            SecretReference = endpoint.SecretReference,
            Status = "active",
            EventTypes = endpoint.EventTypes,
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.CreatedAt,
        };
        dbContext.WebhookEndpoints.Add(record);
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry, projectId.Value));
        await dbContext.SaveChangesAsync(cancellationToken);
        return AdminWebhookEndpointStoreResult.Updated(ToReadModel(record));
    }

    public async Task<AdminWebhookEndpointStoreResult> UpdateAsync(
        Guid endpointId,
        long expectedVersion,
        AdminWebhookEndpointUpdate update,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var endpoint = await FindMutableAsync(endpointId, expectedVersion, cancellationToken);
        if (endpoint.Result is not null)
        {
            return endpoint.Result;
        }

        endpoint.Record!.Url = update.Url;
        endpoint.Record.EventTypes = update.EventTypes;
        endpoint.Record.UpdatedAt = occurredAt;
        endpoint.Record.Version++;
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry, endpoint.Record.ProjectId));
        return await SaveMutationAsync(endpointId, endpoint.Record, cancellationToken);
    }

    public async Task<AdminWebhookEndpointStoreResult> RotateSecretAsync(
        Guid endpointId,
        long expectedVersion,
        string secretReference,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var endpoint = await FindMutableAsync(endpointId, expectedVersion, cancellationToken);
        if (endpoint.Result is not null)
        {
            return endpoint.Result;
        }

        endpoint.Record!.SecretReference = secretReference;
        endpoint.Record.UpdatedAt = occurredAt;
        endpoint.Record.Version++;
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry, endpoint.Record.ProjectId));
        return await SaveMutationAsync(endpointId, endpoint.Record, cancellationToken);
    }

    public async Task<AdminWebhookEndpointStoreResult> DisableAsync(
        Guid endpointId,
        long expectedVersion,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var endpoint = await FindMutableAsync(endpointId, expectedVersion, cancellationToken);
        if (endpoint.Result is not null)
        {
            return endpoint.Result;
        }

        endpoint.Record!.Status = "disabled";
        endpoint.Record.UpdatedAt = occurredAt;
        endpoint.Record.Version++;
        dbContext.AuditLogEntries.Add(ToAuditRecord(auditEntry, endpoint.Record.ProjectId));
        return await SaveMutationAsync(endpointId, endpoint.Record, cancellationToken);
    }

    private async Task<(WebhookEndpointRecord? Record, AdminWebhookEndpointStoreResult? Result)> FindMutableAsync(
        Guid endpointId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var endpoint = await dbContext.WebhookEndpoints
            .SingleOrDefaultAsync(candidate => candidate.Id == endpointId, cancellationToken);
        if (endpoint is null)
        {
            return (null, AdminWebhookEndpointStoreResult.NotFound());
        }

        if (endpoint.Status == "disabled")
        {
            return (null, AdminWebhookEndpointStoreResult.AlreadyDisabled(ToReadModel(endpoint)));
        }

        if (endpoint.Version != expectedVersion)
        {
            return (null, AdminWebhookEndpointStoreResult.ConcurrencyConflict(ToReadModel(endpoint)));
        }

        return (endpoint, null);
    }

    private async Task<AdminWebhookEndpointStoreResult> SaveMutationAsync(
        Guid endpointId,
        WebhookEndpointRecord endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return AdminWebhookEndpointStoreResult.Updated(ToReadModel(endpoint));
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.WebhookEndpoints
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == endpointId, cancellationToken);
            return current is null
                ? AdminWebhookEndpointStoreResult.NotFound()
                : AdminWebhookEndpointStoreResult.ConcurrencyConflict(ToReadModel(current));
        }
    }

    private static AdminWebhookEndpointReadModel ToReadModel(WebhookEndpointRecord endpoint) =>
        new(
            endpoint.ProjectId,
            endpoint.Id,
            endpoint.IntegrationApiCredentialId,
            endpoint.Url,
            endpoint.SecretReference,
            endpoint.Status,
            ParseEventTypes(endpoint.EventTypes),
            endpoint.CreatedAt,
            endpoint.UpdatedAt,
            endpoint.Version);

    private static IReadOnlyList<string> ParseEventTypes(string? eventTypes) =>
        string.IsNullOrWhiteSpace(eventTypes)
            ? []
            : eventTypes.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static AuditLogEntryRecord ToAuditRecord(AdminAuditEntry auditEntry, Guid projectId) =>
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
