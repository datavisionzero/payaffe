namespace Payaffe.Application.Admin;

public interface IAdminWebhookEndpointStore
{
    Task<IReadOnlyList<AdminWebhookEndpointReadModel>> ListAsync(
        Guid? integrationApiCredentialId,
        CancellationToken cancellationToken);

    Task<AdminWebhookEndpointStoreResult> CreateAsync(
        AdminWebhookEndpointDraft endpoint,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<AdminWebhookEndpointStoreResult> UpdateAsync(
        Guid endpointId,
        long expectedVersion,
        AdminWebhookEndpointUpdate update,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<AdminWebhookEndpointStoreResult> RotateSecretAsync(
        Guid endpointId,
        long expectedVersion,
        string secretReference,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<AdminWebhookEndpointStoreResult> DisableAsync(
        Guid endpointId,
        long expectedVersion,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);
}

public sealed record AdminWebhookEndpointStoreResult(
    AdminWebhookEndpointStoreResultKind Kind,
    AdminWebhookEndpointReadModel? Endpoint)
{
    public static AdminWebhookEndpointStoreResult Updated(AdminWebhookEndpointReadModel endpoint) =>
        new(AdminWebhookEndpointStoreResultKind.Updated, endpoint);

    public static AdminWebhookEndpointStoreResult NotFound() =>
        new(AdminWebhookEndpointStoreResultKind.NotFound, Endpoint: null);

    public static AdminWebhookEndpointStoreResult ParentCredentialUnavailable() =>
        new(AdminWebhookEndpointStoreResultKind.ParentCredentialUnavailable, Endpoint: null);

    public static AdminWebhookEndpointStoreResult ConcurrencyConflict(AdminWebhookEndpointReadModel endpoint) =>
        new(AdminWebhookEndpointStoreResultKind.ConcurrencyConflict, endpoint);

    public static AdminWebhookEndpointStoreResult AlreadyDisabled(AdminWebhookEndpointReadModel endpoint) =>
        new(AdminWebhookEndpointStoreResultKind.AlreadyDisabled, endpoint);
}

public enum AdminWebhookEndpointStoreResultKind
{
    Updated,
    NotFound,
    ParentCredentialUnavailable,
    ConcurrencyConflict,
    AlreadyDisabled,
}
