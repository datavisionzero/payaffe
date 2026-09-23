using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Infrastructure.Webhooks;

public sealed class ConfigurationWebhookSecretResolver(IConfiguration configuration) : IWebhookSecretResolver
{
    private const string ReferencePrefix = "configuration:";
    private const string AllowedConfigurationPrefix = "Webhooks:EndpointSecrets:";
    private const string AllowedProjectsPrefix = "Webhooks:Projects:";

    public Task<string?> ResolveForProjectAsync(
        Guid projectId,
        string secretReference,
        CancellationToken cancellationToken)
    {
        var configurationKey = GetConfigurationKey(secretReference);
        if (configurationKey is null || !IsAllowedForProject(projectId, configurationKey))
        {
            return Task.FromResult<string?>(null);
        }

        var secret = configuration[configurationKey];
        return Task.FromResult(string.IsNullOrWhiteSpace(secret) ? null : secret);
    }

    private static bool IsAllowedForProject(Guid projectId, string configurationKey)
    {
        var projectPrefix = $"Webhooks:Projects:{projectId:D}:EndpointSecrets:";
        return configurationKey.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase) ||
               projectId == ProjectDefaults.DefaultProjectId &&
               configurationKey.StartsWith(AllowedConfigurationPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetConfigurationKey(string secretReference)
    {
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return null;
        }

        var trimmedReference = secretReference.Trim();
        if (!trimmedReference.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var configurationKey = trimmedReference[ReferencePrefix.Length..].Trim();
        if (!configurationKey.StartsWith(AllowedConfigurationPrefix, StringComparison.OrdinalIgnoreCase) &&
            !configurationKey.StartsWith(AllowedProjectsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return configurationKey.Length == AllowedConfigurationPrefix.Length ||
               configurationKey.Length == AllowedProjectsPrefix.Length
            ? null
            : configurationKey;
    }
}
