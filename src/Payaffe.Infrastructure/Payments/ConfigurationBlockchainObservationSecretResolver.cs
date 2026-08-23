using Payaffe.Application.Payments;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Infrastructure.Payments;

public sealed class ConfigurationBlockchainObservationSecretResolver(IConfiguration configuration)
    : IBlockchainObservationSecretResolver
{
    private const string ReferencePrefix = "configuration:";
    private const string AllowedConfigurationPrefix = "BlockchainObservation:ProviderSecrets:";

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

    public static string? GetConfigurationKey(string secretReference)
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
