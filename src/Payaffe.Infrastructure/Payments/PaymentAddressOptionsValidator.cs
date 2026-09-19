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
        if (options.NativeEthNetwork is not ("mainnet" or "testnet"))
        {
            failures.Add("PaymentAddresses:NativeEthNetwork must be mainnet or testnet.");
        }

        if (options.NativeEthChainId <= 0)
        {
            failures.Add("PaymentAddresses:NativeEthChainId must be greater than zero.");
        }

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

        var network = ResolveNetwork(supportedCurrency, source.Network);
        try
        {
            _ = ExtPubKey.Parse(source.ExtendedPublicKey, network);
        }
        catch (FormatException)
        {
            failures.Add(Describe(supportedCurrency, source.ExtendedPublicKey, network));
        }
    }

    /// <summary>
    /// Why the value was not taken, in terms the operator can act on. A key the
    /// wallet exported under a prefix this currency does not take is the common
    /// case — a native segwit Bitcoin wallet hands out a `zpub` while
    /// <see cref="WatchOnlyWalletSourceOptions.AddressType"/>, not the prefix,
    /// is what decides the script payaffe derives — and the fix is to re-encode
    /// the same account node, not to find a different wallet.
    /// </summary>
    private static string Describe(string supportedCurrency, string value, Network network)
    {
        const string Setting = "ExtendedPublicKey";
        var identified = ExtendedPublicKeyPrefix.Identify(value);
        if (identified is not { } prefix)
        {
            return $"PaymentAddresses:{supportedCurrency}:{Setting} is not valid for the configured network.";
        }

        if (prefix.Spending)
        {
            return $"PaymentAddresses:{supportedCurrency}:{Setting} begins with {prefix.Name}, "
                + "which is an extended private key. payaffe takes an extended public key only and "
                + "holds nothing that can spend.";
        }

        var accepted = ExtendedPublicKeyPrefix.AcceptedFor(value, network);
        var taken = accepted.Count == 0
            ? "no prefix this knows"
            : string.Join(" or ", accepted);

        return $"PaymentAddresses:{supportedCurrency}:{Setting} begins with {prefix.Name}, "
            + $"which {supportedCurrency} on this network does not take; it takes {taken}. "
            + "The prefix carries no key material, so re-encode the same account node behind "
            + "an accepted one; AddressType decides whether segwit or legacy addresses are derived.";
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
