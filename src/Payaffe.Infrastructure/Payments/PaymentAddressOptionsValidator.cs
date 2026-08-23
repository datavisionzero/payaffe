using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.Altcoins;

namespace Payaffe.Infrastructure.Payments;

public sealed class PaymentAddressOptionsValidator : IValidateOptions<PaymentAddressOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentAddressOptions options)
    {
        var failures = new List<string>();
        ValidateSource("BTC", options.Btc, failures);
        ValidateSource("LTC", options.Ltc, failures);
        if (options.NativeEthLowCapacityThreshold < 0)
        {
            failures.Add("PaymentAddresses:NativeEthLowCapacityThreshold must not be negative.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSource(
        string supportedCurrency,
        WatchOnlyWalletSourceOptions source,
        ICollection<string> failures)
    {
        if (!source.Enabled)
        {
            return;
        }

        if (source.StartingIndex is < 0 or > 2_147_483_647)
        {
            failures.Add(
                $"PaymentAddresses:{supportedCurrency}:StartingIndex must be between 0 and 2147483647.");
        }

        if (source.Network is not ("mainnet" or "testnet"))
        {
            failures.Add(
                $"PaymentAddresses:{supportedCurrency}:Network must be mainnet or testnet.");
            return;
        }

        if (source.AddressType is not ("segwit" or "legacy"))
        {
            failures.Add(
                $"PaymentAddresses:{supportedCurrency}:AddressType must be segwit or legacy.");
        }

        if (string.IsNullOrWhiteSpace(source.ExtendedPublicKey))
        {
            failures.Add(
                $"PaymentAddresses:{supportedCurrency}:ExtendedPublicKey is required when enabled.");
            return;
        }

        try
        {
            _ = ExtPubKey.Parse(source.ExtendedPublicKey, ResolveNetwork(supportedCurrency, source.Network));
        }
        catch (FormatException)
        {
            failures.Add(
                $"PaymentAddresses:{supportedCurrency}:ExtendedPublicKey is not valid for the configured network.");
        }
    }

    internal static Network ResolveNetwork(string supportedCurrency, string network)
    {
        var mainnet = StringComparer.Ordinal.Equals(network, "mainnet");
        return supportedCurrency switch
        {
            "BTC" => mainnet ? Network.Main : Network.TestNet,
            "LTC" => mainnet ? Litecoin.Instance.Mainnet : Litecoin.Instance.Testnet,
            _ => throw new InvalidOperationException(
                $"Unsupported Watch-Only Wallet Source currency '{supportedCurrency}'."),
        };
    }
}
