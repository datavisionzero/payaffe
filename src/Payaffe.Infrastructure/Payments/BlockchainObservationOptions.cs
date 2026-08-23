namespace Payaffe.Infrastructure.Payments;

public sealed class BlockchainObservationOptions
{
    public string Mode { get; set; } = "none";

    public BlockchainObservationProviderOptions Blockchair { get; set; } = new();

    public NownodesBlockchainObservationProviderOptions Nownodes { get; set; } = new();

    public static string NormalizeMode(string? mode)
    {
        return string.IsNullOrWhiteSpace(mode)
            ? "none"
            : mode.Trim().ToLowerInvariant();
    }
}

public sealed class BlockchainObservationProviderOptions
{
    public Uri? BaseUrl { get; set; }

    public string? ApiKeyReference { get; set; }

    public int MaxTransactionsPerAddressPoll { get; set; } = 10;
}

public sealed class NownodesBlockchainObservationProviderOptions
{
    public Uri? BtcBaseUrl { get; set; }

    public Uri? LtcBaseUrl { get; set; }

    public Uri? EthBaseUrl { get; set; }

    public string? ApiKeyReference { get; set; }

    public int MaxTransactionsPerAddressPoll { get; set; } = 10;
}
