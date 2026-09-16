using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;

namespace Payaffe.Application.Admin;

public sealed class AdminWebhookEndpointService(
    IAdminWebhookEndpointStore store,
    IWebhookSecretResolver secretResolver,
    IClock clock)
{
    private const int MaxUrlLength = 2048;
    private static readonly HashSet<string> SupportedEventTypes =
    [
        "payment.created",
        "payment.currency_selected",
        "payment.observed",
        "payment.completed",
        "payment.expired",
        "payment.settled",
    ];

    public Task<IReadOnlyList<AdminWebhookEndpointReadModel>> ListAsync(
        Guid projectId,
        Guid? integrationApiCredentialId,
        CancellationToken cancellationToken) =>
        projectId == Guid.Empty
            ? Task.FromResult<IReadOnlyList<AdminWebhookEndpointReadModel>>([])
            : store.ListAsync(projectId, integrationApiCredentialId, cancellationToken);

    public async Task<AdminWebhookEndpointResult> CreateAsync(
        Guid projectId,
        Guid integrationApiCredentialId,
        string? url,
        string? secretReference,
        IReadOnlyList<string>? eventTypes,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizeUrl(url);
        var normalizedSecretReference = secretReference?.Trim();
        var normalizedEventTypes = NormalizeEventTypes(eventTypes);
        if (projectId == Guid.Empty || integrationApiCredentialId == Guid.Empty ||
            normalizedUrl is null ||
            string.IsNullOrWhiteSpace(normalizedSecretReference) ||
            normalizedEventTypes.Invalid)
        {
            return AdminWebhookEndpointResult.InvalidInput();
        }

        if (await secretResolver.ResolveForProjectAsync(projectId, normalizedSecretReference, cancellationToken) is not { Length: > 0 })
        {
            return AdminWebhookEndpointResult.SecretUnavailable();
        }

        var occurredAt = clock.UtcNow;
        var endpointId = Guid.NewGuid();
        var result = await store.CreateAsync(
            projectId,
            new AdminWebhookEndpointDraft(
                endpointId,
                integrationApiCredentialId,
                normalizedUrl,
                normalizedSecretReference,
                normalizedEventTypes.Serialized,
                occurredAt),
            CreateAuditEntry(
                context,
                occurredAt,
                "admin.webhook_endpoint.create",
                "webhook_endpoint.created",
                endpointId),
            cancellationToken);
        return MapStoreResult(result);
    }

    public async Task<AdminWebhookEndpointResult> UpdateAsync(
        Guid projectId,
        Guid endpointId,
        long expectedVersion,
        string? url,
        IReadOnlyList<string>? eventTypes,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizeUrl(url);
        var normalizedEventTypes = NormalizeEventTypes(eventTypes);
        if (projectId == Guid.Empty || endpointId == Guid.Empty ||
            expectedVersion <= 0 ||
            normalizedUrl is null ||
            normalizedEventTypes.Invalid)
        {
            return AdminWebhookEndpointResult.InvalidInput();
        }

        var occurredAt = clock.UtcNow;
        var result = await store.UpdateAsync(
            projectId,
            endpointId,
            expectedVersion,
            new AdminWebhookEndpointUpdate(normalizedUrl, normalizedEventTypes.Serialized),
            occurredAt,
            CreateAuditEntry(
                context,
                occurredAt,
                "admin.webhook_endpoint.update",
                "webhook_endpoint.updated",
                endpointId),
            cancellationToken);
        return MapStoreResult(result);
    }

    public async Task<AdminWebhookEndpointResult> RotateSecretAsync(
        Guid projectId,
        Guid endpointId,
        long expectedVersion,
        string? secretReference,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        var normalizedSecretReference = secretReference?.Trim();
        if (projectId == Guid.Empty || endpointId == Guid.Empty ||
            expectedVersion <= 0 ||
            string.IsNullOrWhiteSpace(normalizedSecretReference))
        {
            return AdminWebhookEndpointResult.InvalidInput();
        }

        if (await secretResolver.ResolveForProjectAsync(projectId, normalizedSecretReference, cancellationToken) is not { Length: > 0 })
        {
            return AdminWebhookEndpointResult.SecretUnavailable();
        }

        var occurredAt = clock.UtcNow;
        var result = await store.RotateSecretAsync(
            projectId,
            endpointId,
            expectedVersion,
            normalizedSecretReference,
            occurredAt,
            CreateAuditEntry(
                context,
                occurredAt,
                "admin.webhook_endpoint.rotate_secret",
                "webhook_endpoint.secret_rotated",
                endpointId),
            cancellationToken);
        return MapStoreResult(result);
    }

    public async Task<AdminWebhookEndpointResult> DisableAsync(
        Guid projectId,
        Guid endpointId,
        long expectedVersion,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || endpointId == Guid.Empty || expectedVersion <= 0)
        {
            return AdminWebhookEndpointResult.InvalidInput();
        }

        var occurredAt = clock.UtcNow;
        var result = await store.DisableAsync(
            projectId,
            endpointId,
            expectedVersion,
            occurredAt,
            CreateAuditEntry(
                context,
                occurredAt,
                "admin.webhook_endpoint.disable",
                "webhook_endpoint.disabled",
                endpointId),
            cancellationToken);
        return MapStoreResult(result);
    }

    private static string? NormalizeUrl(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.Length > MaxUrlLength ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("https" or "http") ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            return null;
        }

        return parsed.AbsoluteUri;
    }

    private static (bool Invalid, string? Serialized) NormalizeEventTypes(
        IReadOnlyList<string>? eventTypes)
    {
        if (eventTypes is null or { Count: 0 })
        {
            return (false, null);
        }

        var normalized = eventTypes
            .Select(eventType => eventType?.Trim())
            .ToArray();
        if (normalized.Any(eventType =>
                string.IsNullOrWhiteSpace(eventType) ||
                !SupportedEventTypes.Contains(eventType)))
        {
            return (true, null);
        }

        return (false, string.Join(',', normalized.Distinct(StringComparer.Ordinal).Order()));
    }

    private static AdminWebhookEndpointResult MapStoreResult(AdminWebhookEndpointStoreResult result) =>
        result.Kind switch
        {
            AdminWebhookEndpointStoreResultKind.Updated =>
                AdminWebhookEndpointResult.Success(result.Endpoint!),
            AdminWebhookEndpointStoreResultKind.NotFound =>
                AdminWebhookEndpointResult.NotFound(),
            AdminWebhookEndpointStoreResultKind.ParentCredentialUnavailable =>
                AdminWebhookEndpointResult.ParentCredentialUnavailable(),
            AdminWebhookEndpointStoreResultKind.ConcurrencyConflict =>
                AdminWebhookEndpointResult.ConcurrencyConflict(result.Endpoint!),
            AdminWebhookEndpointStoreResultKind.AlreadyDisabled =>
                AdminWebhookEndpointResult.AlreadyDisabled(result.Endpoint!),
            _ => throw new InvalidOperationException($"Unsupported webhook endpoint store result {result.Kind}."),
        };

    private static AdminAuditEntry CreateAuditEntry(
        AdminOperationContext context,
        DateTimeOffset occurredAt,
        string eventType,
        string reasonCode,
        Guid endpointId) =>
        new(
            Guid.NewGuid(),
            occurredAt,
            eventType,
            "success",
            "product_user",
            context.AdminAccountId.ToString("D"),
            "api",
            context.SourceIp,
            context.UserAgent,
            context.CorrelationId,
            reasonCode,
            "webhook_endpoint",
            endpointId.ToString("D"));
}
