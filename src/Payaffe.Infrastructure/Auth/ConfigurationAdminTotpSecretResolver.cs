using Payaffe.Application.Admin;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Infrastructure.Auth;

public sealed class ConfigurationAdminTotpSecretResolver(IConfiguration configuration)
    : IAdminTotpSecretResolver
{
    private const string ReferencePrefix = "configuration:";
    private const string AllowedConfigurationPrefix = "Admin:TotpSecrets:";

    public Task<byte[]?> ResolveSecretAsync(
        string secretReference,
        CancellationToken cancellationToken)
    {
        var configurationKey = GetConfigurationKey(secretReference);
        if (configurationKey is null)
        {
            return Task.FromResult<byte[]?>(null);
        }

        var encodedSecret = configuration[configurationKey];
        return Task.FromResult(TryDecodeBase32(encodedSecret));
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

    private static byte[]? TryDecodeBase32(string? encodedSecret)
    {
        if (string.IsNullOrWhiteSpace(encodedSecret))
        {
            return null;
        }

        var normalized = encodedSecret
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray();
        while (normalized.Length > 0 && normalized[^1] == '=')
        {
            normalized = normalized[..^1];
        }

        if (normalized.Length == 0)
        {
            return null;
        }

        var output = new byte[normalized.Length * 5 / 8];
        var buffer = 0;
        var bitsInBuffer = 0;
        var outputIndex = 0;

        foreach (var character in normalized)
        {
            var value = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= '2' and <= '7' => character - '2' + 26,
                _ => -1,
            };
            if (value < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | value;
            bitsInBuffer += 5;
            if (bitsInBuffer < 8)
            {
                continue;
            }

            bitsInBuffer -= 8;
            output[outputIndex++] = (byte)(buffer >> bitsInBuffer);
            buffer &= (1 << bitsInBuffer) - 1;
        }

        return outputIndex < 20 ? null : output[..outputIndex];
    }
}
