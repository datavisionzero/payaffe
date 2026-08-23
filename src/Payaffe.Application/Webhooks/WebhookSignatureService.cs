using System.Security.Cryptography;
using System.Text;

namespace Payaffe.Application.Webhooks;

public static class WebhookSignatureService
{
    public static string CreateSignature(string secret, DateTimeOffset timestamp, string rawBody)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Webhook secret is required.", nameof(secret));
        }

        var unixTimestamp = timestamp.ToUnixTimeSeconds();
        var signatureBase = $"{unixTimestamp}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase));
        return "v1=" + Convert.ToHexString(signature).ToLowerInvariant();
    }
}
