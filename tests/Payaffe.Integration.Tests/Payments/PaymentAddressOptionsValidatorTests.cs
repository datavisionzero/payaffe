using Microsoft.Extensions.Options;
using Payaffe.Infrastructure.Payments;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.DataEncoders;

namespace Payaffe.Integration.Tests.Payments;

public sealed class PaymentAddressOptionsValidatorTests
{
    [Fact]
    public void Accepts_public_watch_only_source()
    {
        var result = new PaymentAddressOptionsValidator().Validate(
            null,
            new PaymentAddressOptions
            {
                Btc = new WatchOnlyWalletSourceOptions
                {
                    Enabled = true,
                    ExtendedPublicKey = new ExtKey().Neuter().ToString(Network.TestNet),
                    Network = "testnet",
                    AddressType = "segwit",
                },
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Rejects_extended_private_key_and_invalid_derivation_configuration()
    {
        var result = new PaymentAddressOptionsValidator().Validate(
            null,
            new PaymentAddressOptions
            {
                Btc = new WatchOnlyWalletSourceOptions
                {
                    Enabled = true,
                    ExtendedPublicKey = new ExtKey().ToString(Network.Main),
                    Network = "mainnet",
                    AddressType = "private",
                    StartingIndex = -1,
                },
                NativeEthLowCapacityThreshold = -1,
            });

        Assert.False(result.Succeeded);
        Assert.Contains("ExtendedPublicKey", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("AddressType", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("StartingIndex", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("NativeEthLowCapacityThreshold", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_invalid_native_eth_network_and_chain_id()
    {
        var result = new PaymentAddressOptionsValidator().Validate(
            null,
            new PaymentAddressOptions
            {
                NativeEthNetwork = "sepolia",
                NativeEthChainId = 0,
            });

        Assert.False(result.Succeeded);
        Assert.Contains("NativeEthNetwork", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("NativeEthChainId", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prefixes each currency takes, measured rather than assumed. NBitcoin
    /// decides this, so an upgrade that changed it would surface here instead of
    /// at an installation that pasted a key the product no longer reads.
    /// </summary>
    [Theory]
    [InlineData("BTC", "mainnet", "xpub", true)]
    [InlineData("BTC", "mainnet", "Ltub", false)]
    [InlineData("BTC", "mainnet", "zpub", false)]
    [InlineData("BTC", "mainnet", "ypub", false)]
    [InlineData("BTC", "testnet", "tpub", true)]
    [InlineData("BTC", "testnet", "ttub", false)]
    [InlineData("LTC", "mainnet", "xpub", true)]
    [InlineData("LTC", "mainnet", "Ltub", true)]
    [InlineData("LTC", "mainnet", "Mtub", false)]
    [InlineData("LTC", "testnet", "ttub", true)]
    public void Takes_the_prefixes_the_currency_serialises(
        string supportedCurrency,
        string network,
        string prefix,
        bool accepted)
    {
        var result = Validate(supportedCurrency, network, Reserialise(PublicKey, prefix));

        Assert.Equal(accepted, result.Succeeded);
    }

    /// <summary>
    /// A Litecoin account key configured as the BTC source would derive BTC
    /// addresses out of a Litecoin key tree, so the payer pays an address the
    /// operator's Bitcoin wallet cannot see.
    /// </summary>
    [Fact]
    public void Rejects_a_litecoin_prefix_as_the_bitcoin_source()
    {
        var result = Validate("BTC", "mainnet", Reserialise(PublicKey, "Ltub"));

        Assert.False(result.Succeeded);
        Assert.Contains("Ltub", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("xpub", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rejection an operator is most likely to meet: AddressType defaults to
    /// segwit, and a native segwit wallet exports the account as `zpub`.
    /// </summary>
    [Fact]
    public void Names_the_form_a_native_segwit_export_has_to_be_converted_to()
    {
        var result = Validate("BTC", "mainnet", Reserialise(PublicKey, "zpub"));

        Assert.False(result.Succeeded);
        Assert.Contains("zpub", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("xpub", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("AddressType", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_an_extended_private_key_as_such()
    {
        var result = Validate("BTC", "mainnet", new ExtKey().ToString(Network.Main));

        Assert.False(result.Succeeded);
        Assert.Contains("extended private key", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// What validation cannot do. NBitcoin serialises a Litecoin extended public
    /// key behind Bitcoin's version bytes, so a Litecoin account key and a
    /// Bitcoin one are the same string — nothing in the value says which chain
    /// the operator took it from. Only the operator can get that right, which is
    /// why the deployment documentation says so rather than the validator.
    /// </summary>
    [Fact]
    public void Cannot_tell_a_litecoin_account_key_from_a_bitcoin_one()
    {
        var key = new ExtKey().Neuter();

        Assert.Equal(key.ToString(Network.Main), key.ToString(Litecoin.Instance.Mainnet));
        Assert.True(Validate("BTC", "mainnet", key.ToString(Litecoin.Instance.Mainnet)).Succeeded);
    }

    private static readonly string PublicKey = new ExtKey().Neuter().ToString(Network.Main);

    private static ValidateOptionsResult Validate(
        string supportedCurrency,
        string network,
        string extendedPublicKey)
    {
        var source = new WatchOnlyWalletSourceOptions
        {
            Enabled = true,
            ExtendedPublicKey = extendedPublicKey,
            Network = network,
            AddressType = "segwit",
        };

        return new PaymentAddressOptionsValidator().Validate(
            null,
            supportedCurrency == "BTC"
                ? new PaymentAddressOptions { Btc = source }
                : new PaymentAddressOptions { Ltc = source });
    }

    /// <summary>
    /// The same key behind another prefix's version bytes. Re-encoding is all
    /// there is to it, which is the point the failure message makes to the
    /// operator.
    /// </summary>
    private static string Reserialise(string extendedKey, string prefix)
    {
        var version = prefix switch
        {
            "xpub" => 0x0488B21Eu,
            "ypub" => 0x049D7CB2u,
            "zpub" => 0x04B24746u,
            "tpub" => 0x043587CFu,
            "Ltub" => 0x019DA462u,
            "Mtub" => 0x01B26EF6u,
            "ttub" => 0x0436F6E1u,
            _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Unknown prefix."),
        };

        var body = Encoders.Base58Check.DecodeData(extendedKey);
        body[0] = (byte)(version >> 24);
        body[1] = (byte)(version >> 16);
        body[2] = (byte)(version >> 8);
        body[3] = (byte)version;

        var reserialised = Encoders.Base58Check.EncodeData(body);
        Assert.StartsWith(prefix, reserialised, StringComparison.Ordinal);
        return reserialised;
    }
}
