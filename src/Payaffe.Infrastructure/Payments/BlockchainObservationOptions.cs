namespace Payaffe.Infrastructure.Payments;

public sealed class BlockchainObservationOptions
{
    /// <summary>The only mode of a Test Mode installation (ADR 0033).</summary>
    public const string SimulatedMode = "simulated";

    public string Mode { get; set; } = "none";

    public BlockchainObservationProviderOptions Blockchair { get; set; } = new();

    public NownodesBlockchainObservationProviderOptions Nownodes { get; set; } = new();

    /// <summary>
    /// Whether a mode supplies Blockchain Truth at all, as opposed to
    /// <c>none</c>.
    /// </summary>
    public static bool IsObserving(string? mode) =>
        NormalizeMode(mode) is "blockchair" or "nownodes" or SimulatedMode;

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
