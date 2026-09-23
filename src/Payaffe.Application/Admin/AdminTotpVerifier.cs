using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Payaffe.Application.Admin;

public sealed class AdminTotpVerifier : IAdminTotpVerifier
{
    private const long UnixEpochTicks = 621355968000000000L;
    private const long TicksPerTimeStep = TimeSpan.TicksPerSecond * 30;

    public bool TryVerifyCode(
        byte[] secret,
        string code,
        DateTimeOffset now,
        int allowedTimeStepSkew,
        out long timeStep)
    {
        timeStep = 0;
        var normalizedCode = code.Trim();
        if (normalizedCode.Length != 6 || normalizedCode.Any(character => !char.IsDigit(character)))
        {
            return false;
        }

        var currentTimeStep = (now.UtcTicks - UnixEpochTicks) / TicksPerTimeStep;
        for (var offset = -allowedTimeStepSkew; offset <= allowedTimeStepSkew; offset++)
        {
            var candidate = ComputeCode(secret, currentTimeStep + offset);
            if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(normalizedCode),
                System.Text.Encoding.ASCII.GetBytes(candidate)))
            {
                timeStep = currentTimeStep + offset;
                return true;
            }
        }

        return false;
    }

    private static string ComputeCode(byte[] secret, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);
        return (binaryCode % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
