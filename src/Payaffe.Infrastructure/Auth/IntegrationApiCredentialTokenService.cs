using System.Security.Cryptography;
using Payaffe.Application.Admin;

namespace Payaffe.Infrastructure.Auth;

public sealed class IntegrationApiCredentialTokenService : IIntegrationApiCredentialTokenService
{
    public string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return "payaffe_integration_" + encoded;
    }

    public string HashToken(string token) =>
        IntegrationApiCredentialTokenHasher.HashToken(token);
}
