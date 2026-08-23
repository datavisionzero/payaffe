using System.Security.Cryptography;
using System.Text;
using Payaffe.Application.Admin;

namespace Payaffe.Infrastructure.Auth;

public sealed class AdminSessionTokenService : IAdminSessionTokenService
{
    public string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
