using Payaffe.Infrastructure.Payments;
using NBitcoin;

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
}
