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
    string ExpectedCryptoAmount);

public sealed record BlockchainObservation(
    string TransactionHash,
    string ObservedAmount,
    DateTimeOffset ObservedAt,
    int Confirmations,
    string ProviderName,
    string? ProviderObservationId);
