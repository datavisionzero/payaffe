namespace Payaffe.Application.Payments;

public interface IBlockchainObservationAdapter
{
    Task StartWatchingAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockchainObservation>> PollAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken);

    Task<bool> IsObservationAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

public sealed record BlockchainObservationTarget(
    Guid PaymentId,
    string SupportedCurrency,
    string PaymentAddress,
    string ExpectedCryptoAmount,
    Guid ProjectId = default);

/// <summary>
/// One Blockchain Transaction as the provider reports it for a Payment Address.
/// The block it is in is null while it is unconfirmed or when the provider does
/// not say.
/// </summary>
public sealed record BlockchainObservation(
    string TransactionHash,
    string ObservedAmount,
    DateTimeOffset ObservedAt,
    int Confirmations,
    string ProviderName,
    string? ProviderObservationId,
    string? BlockHash = null,
    long? BlockHeight = null);
