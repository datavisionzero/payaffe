using System.Security.Cryptography;

namespace Payaffe.Application.Payments;

public interface IPayerPageIdGenerator
{
    string Generate();
}

public sealed class PayerPageIdGenerator : IPayerPageIdGenerator
{
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyz23456789";
    private const int Length = 16;

    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[Length];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[Length];
        for (var index = 0; index < bytes.Length; index++)
        {
            chars[index] = Alphabet[bytes[index] % Alphabet.Length];
        }

        return new string(chars);
    }
}
