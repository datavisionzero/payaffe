using System.Numerics;

namespace Payaffe.Application.Payments;

internal static class PaymentInstructionFactory
{
    public static string ToAtomicAmount(string supportedCurrency, string amount)
    {
        var scale = supportedCurrency switch
        {
            "BTC" or "LTC" => 8,
            "ETH" => 18,
            _ => throw new InvalidOperationException(
                $"Unsupported Payment Instruction currency '{supportedCurrency}'."),
        };

        var parts = amount.Split('.', StringSplitOptions.None);
        if (parts.Length is < 1 or > 2 ||
            parts[0].Length == 0 ||
            parts[0].Any(character => !char.IsAsciiDigit(character)) ||
            (parts[0].Length > 1 && parts[0][0] == '0'))
        {
            throw new InvalidOperationException("Cryptocurrency amount is not a canonical decimal value.");
        }

        var fraction = parts.Length == 2 ? parts[1] : string.Empty;
        if ((parts.Length == 2 && fraction.Length == 0) ||
            fraction.Length > scale ||
            fraction.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new InvalidOperationException(
                $"Cryptocurrency amount exceeds the {supportedCurrency} precision.");
        }

        var atomicDigits = parts[0] + fraction.PadRight(scale, '0');
        var firstNonZero = atomicDigits.AsSpan().IndexOfAnyExcept('0');
        return firstNonZero < 0 ? "0" : atomicDigits[firstNonZero..];
    }

    public static string BuildUri(PaymentInstructionReadModel instruction)
    {
        ValidateNetwork(instruction.Network);
        var amountAtomic = ToAtomicAmount(instruction.SupportedCurrency, instruction.Amount);
        return instruction.SupportedCurrency switch
        {
            "BTC" => BuildBitcoinUri(instruction),
            "LTC" => BuildLitecoinUri(instruction),
            "ETH" => BuildEthereumUri(instruction, amountAtomic),
            _ => throw new InvalidOperationException(
                $"Unsupported Payment Instruction currency '{instruction.SupportedCurrency}'."),
        };
    }

    public static string? GetObservedAmountState(
        string? supportedCurrency,
        string? expectedAmount,
        string? observedAmount)
    {
        if (supportedCurrency is null || expectedAmount is null)
        {
            return null;
        }

        if (observedAmount is null)
        {
            return "none";
        }

        var expectedAtomic = BigInteger.Parse(ToAtomicAmount(supportedCurrency, expectedAmount));
        var observedAtomic = BigInteger.Parse(ToAtomicAmount(supportedCurrency, observedAmount));
        return observedAtomic.CompareTo(expectedAtomic) switch
        {
            < 0 => "underpaid",
            0 => "exact",
            > 0 => "overpaid",
        };
    }

    private static string BuildBitcoinUri(PaymentInstructionReadModel instruction)
    {
        var address = instruction.PaymentAddress;
        var lowerAddress = address.ToLowerInvariant();
        var isMainnetAddress = lowerAddress.StartsWith("bc1", StringComparison.Ordinal) ||
            address.StartsWith('1') ||
            address.StartsWith('3');
        var isTestnetAddress = lowerAddress.StartsWith("tb1", StringComparison.Ordinal) ||
            address.StartsWith('m') ||
            address.StartsWith('n') ||
            address.StartsWith('2');
        EnsureAddressMatchesNetwork(instruction, isMainnetAddress, isTestnetAddress);

        return instruction.Network == "testnet" && lowerAddress.StartsWith("tb1", StringComparison.Ordinal)
            ? $"bitcoin:?tb={address}&amount={instruction.Amount}"
            : $"bitcoin:{address}?amount={instruction.Amount}";
    }

    private static string BuildLitecoinUri(PaymentInstructionReadModel instruction)
    {
        var address = instruction.PaymentAddress;
        var lowerAddress = address.ToLowerInvariant();
        var isMainnetAddress = lowerAddress.StartsWith("ltc1", StringComparison.Ordinal) ||
            address.StartsWith('L') ||
            address.StartsWith('M');
        var isTestnetAddress = lowerAddress.StartsWith("tltc1", StringComparison.Ordinal) ||
            address.StartsWith('m') ||
            address.StartsWith('n') ||
            address.StartsWith('Q');
        EnsureAddressMatchesNetwork(instruction, isMainnetAddress, isTestnetAddress);
        return $"litecoin:{address}?amount={instruction.Amount}";
    }

    private static string BuildEthereumUri(
        PaymentInstructionReadModel instruction,
        string amountAtomic)
    {
        if (instruction.ChainId is null or <= 0)
        {
            throw new InvalidOperationException("Native ETH Payment Instruction requires a chain ID.");
        }

        var address = instruction.PaymentAddress;
        if (address.Length != 42 ||
            !address.StartsWith("0x", StringComparison.Ordinal) ||
            address[2..].Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidOperationException("Native ETH Payment Address is invalid.");
        }

        return $"ethereum:{address}@{instruction.ChainId.Value}?value={amountAtomic}";
    }

    private static void EnsureAddressMatchesNetwork(
        PaymentInstructionReadModel instruction,
        bool isMainnetAddress,
        bool isTestnetAddress)
    {
        var matches = instruction.Network switch
        {
            "mainnet" => isMainnetAddress,
            "testnet" => isTestnetAddress,
            _ => false,
        };
        if (!matches)
        {
            throw new InvalidOperationException(
                $"{instruction.SupportedCurrency} Payment Address does not match the snapshotted network.");
        }
    }

    private static void ValidateNetwork(string network)
    {
        if (network is not ("mainnet" or "testnet"))
        {
            throw new InvalidOperationException($"Unsupported Payment Instruction network '{network}'.");
        }
    }
}
