namespace Payaffe.Application.Admin;

public interface IAdminIntegrationApiCredentialStore
{
    Task<IReadOnlyList<AdminIntegrationApiCredentialReadModel>> ListAsync(
        CancellationToken cancellationToken);

    Task<AdminIntegrationApiCredentialReadModel> CreateAsync(
        AdminIntegrationApiCredentialDraft credential,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<AdminIntegrationApiCredentialStoreResult> RotateAsync(
        Guid credentialId,
        long expectedVersion,
        string tokenHash,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<AdminIntegrationApiCredentialStoreResult> DisableAsync(
        Guid credentialId,
        long expectedVersion,
        DateTimeOffset occurredAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);
}

public sealed record AdminIntegrationApiCredentialStoreResult(
    AdminIntegrationApiCredentialStoreResultKind Kind,
    AdminIntegrationApiCredentialReadModel? Credential)
{
    public static AdminIntegrationApiCredentialStoreResult Updated(
        AdminIntegrationApiCredentialReadModel credential) =>
        new(AdminIntegrationApiCredentialStoreResultKind.Updated, credential);

    public static AdminIntegrationApiCredentialStoreResult NotFound() =>
        new(AdminIntegrationApiCredentialStoreResultKind.NotFound, Credential: null);

    public static AdminIntegrationApiCredentialStoreResult ConcurrencyConflict(
        AdminIntegrationApiCredentialReadModel credential) =>
        new(AdminIntegrationApiCredentialStoreResultKind.ConcurrencyConflict, credential);

    public static AdminIntegrationApiCredentialStoreResult AlreadyDisabled(
        AdminIntegrationApiCredentialReadModel credential) =>
        new(AdminIntegrationApiCredentialStoreResultKind.AlreadyDisabled, credential);
}

public enum AdminIntegrationApiCredentialStoreResultKind
{
    Updated,
    NotFound,
    ConcurrencyConflict,
    AlreadyDisabled,
}
