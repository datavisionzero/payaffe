namespace Payaffe.Infrastructure.Payments;

public sealed class PaymentAddressOptions
{
    public WatchOnlyWalletSourceOptions Btc { get; set; } = new();

    public WatchOnlyWalletSourceOptions Ltc { get; set; } = new();

    public string NativeEthNetwork { get; set; } = "mainnet";

    public long NativeEthChainId { get; set; } = 1;

    public int NativeEthLowCapacityThreshold { get; set; } = 20;
}

public sealed class WatchOnlyWalletSourceOptions
{
    public bool Enabled { get; set; }

    public string? ExtendedPublicKey { get; set; }

    public string Network { get; set; } = "mainnet";

    public string AddressType { get; set; } = "segwit";

    public long StartingIndex { get; set; }
}
