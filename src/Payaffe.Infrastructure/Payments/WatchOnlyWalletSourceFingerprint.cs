using System.Security.Cryptography;
using System.Text;

namespace Payaffe.Infrastructure.Payments;

internal static class WatchOnlyWalletSourceFingerprint
{
    public static string Compute(
        string supportedCurrency,
        string network,
        string addressType,
        string extendedPublicKey)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{supportedCurrency}\n{network}\n{addressType}\n{extendedPublicKey}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
