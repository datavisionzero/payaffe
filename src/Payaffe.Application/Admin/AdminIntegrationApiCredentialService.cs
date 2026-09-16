using Payaffe.Application.Payments;

namespace Payaffe.Application.Admin;

public sealed class AdminIntegrationApiCredentialService(
    IAdminIntegrationApiCredentialStore store,
    IIntegrationApiCredentialTokenService tokenService,
    IClock clock)
{
    private const int MaxNameLength = 255;

    public Task<IReadOnlyList<AdminIntegrationApiCredentialReadModel>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        projectId == Guid.Empty
            ? Task.FromResult<IReadOnlyList<AdminIntegrationApiCredentialReadModel>>([])
            : store.ListAsync(projectId, cancellationToken);

    public async Task<AdminIntegrationApiCredentialCreateResult> CreateAsync(
        Guid projectId,
        string? name,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            return AdminIntegrationApiCredentialCreateResult.ProjectUnavailable();
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > MaxNameLength)
        {
            return AdminIntegrationApiCredentialCreateResult.InvalidName();
        }

        var occurredAt = clock.UtcNow;
        var credentialId = Guid.NewGuid();
        var token = tokenService.GenerateToken();
        var credential = await store.CreateAsync(
            new AdminIntegrationApiCredentialDraft(
                projectId,
                credentialId,
                normalizedName,
                tokenService.HashToken(token),
                occurredAt),
            CreateAuditEntry(
                context,
                occurredAt,
                "admin.integration_api_credential.create",
                "success",
                "integration_api_credential.created",
                credentialId),
            cancellationToken);

        if (credential is null)
        {
            return AdminIntegrationApiCredentialCreateResult.ProjectUnavailable();
        }

        return AdminIntegrationApiCredentialCreateResult.Created(credential, token);
    }

    public async Task<AdminIntegrationApiCredentialMutationResult> RotateAsync(
        Guid projectId,
        Guid credentialId,
        long expectedVersion,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || credentialId == Guid.Empty || expectedVersion <= 0)
        {
            return AdminIntegrationApiCredentialMutationResult.InvalidVersion();
        }

        var occurredAt = clock.UtcNow;
        var token = tokenService.GenerateToken();
        var result = await store.RotateAsync(
            projectId,
            credentialId,
            expectedVersion,
            tokenService.HashToken(token),
            occurredAt,
            CreateAuditEntry(
                context,
                occurredAt,
                "admin.integration_api_credential.rotate",
                "success",
                "integration_api_credential.rotated",
                credentialId),
            cancellationToken);

        return result.Kind switch
        {
            AdminIntegrationApiCredentialStoreResultKind.Updated =>
                AdminIntegrationApiCredentialMutationResult.Rotated(result.Credential!, token),
            AdminIntegrationApiCredentialStoreResultKind.NotFound =>
                AdminIntegrationApiCredentialMutationResult.NotFound(),
            AdminIntegrationApiCredentialStoreResultKind.ConcurrencyConflict =>
                AdminIntegrationApiCredentialMutationResult.ConcurrencyConflict(result.Credential!),
            AdminIntegrationApiCredentialStoreResultKind.AlreadyDisabled =>
                AdminIntegrationApiCredentialMutationResult.AlreadyDisabled(result.Credential!),
            _ => throw new InvalidOperationException($"Unsupported credential store result {result.Kind}."),
        };
    }

    public async Task<AdminIntegrationApiCredentialMutationResult> DisableAsync(
        Guid projectId,
        Guid credentialId,
        long expectedVersion,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || credentialId == Guid.Empty || expectedVersion <= 0)
        {
            return AdminIntegrationApiCredentialMutationResult.InvalidVersion();
        }

        var occurredAt = clock.UtcNow;
        var result = await store.DisableAsync(
            projectId,
            credentialId,
            expectedVersion,
            occurredAt,
            CreateAuditEntry(
                context,
                occurredAt,
                "admin.integration_api_credential.disable",
                "success",
                "integration_api_credential.disabled",
                credentialId),
            cancellationToken);

        return result.Kind switch
        {
            AdminIntegrationApiCredentialStoreResultKind.Updated =>
                AdminIntegrationApiCredentialMutationResult.Disabled(result.Credential!),
            AdminIntegrationApiCredentialStoreResultKind.NotFound =>
                AdminIntegrationApiCredentialMutationResult.NotFound(),
            AdminIntegrationApiCredentialStoreResultKind.ConcurrencyConflict =>
                AdminIntegrationApiCredentialMutationResult.ConcurrencyConflict(result.Credential!),
            AdminIntegrationApiCredentialStoreResultKind.AlreadyDisabled =>
                AdminIntegrationApiCredentialMutationResult.AlreadyDisabled(result.Credential!),
            _ => throw new InvalidOperationException($"Unsupported credential store result {result.Kind}."),
        };
    }

    private static AdminAuditEntry CreateAuditEntry(
        AdminOperationContext context,
        DateTimeOffset occurredAt,
        string eventType,
        string outcome,
        string reasonCode,
        Guid credentialId) =>
        new(
            Guid.NewGuid(),
            occurredAt,
            eventType,
            outcome,
            "product_user",
            context.AdminAccountId.ToString("D"),
            "api",
            context.SourceIp,
            context.UserAgent,
            context.CorrelationId,
            reasonCode,
            "integration_api_credential",
            credentialId.ToString("D"));
}
