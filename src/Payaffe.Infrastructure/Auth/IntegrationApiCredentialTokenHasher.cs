using System.Security.Cryptography;
using System.Text;

namespace Payaffe.Infrastructure.Auth;

public static class IntegrationApiCredentialTokenHasher
{
    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
