using Payaffe.Application.Webhooks;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Infrastructure.Webhooks;

public sealed class ConfigurationWebhookSecretResolver(IConfiguration configuration) : IWebhookSecretResolver
{
    private const string ReferencePrefix = "configuration:";
    private const string AllowedConfigurationPrefix = "Webhooks:EndpointSecrets:";

    public Task<string?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken)
    {
        var configurationKey = GetConfigurationKey(secretReference);
        if (configurationKey is null)
        {
            return Task.FromResult<string?>(null);
        }

        var secret = configuration[configurationKey];
        return Task.FromResult(string.IsNullOrWhiteSpace(secret) ? null : secret);
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
        if (!configurationKey.StartsWith(AllowedConfigurationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return configurationKey.Length == AllowedConfigurationPrefix.Length
            ? null
            : configurationKey;
    }
}
