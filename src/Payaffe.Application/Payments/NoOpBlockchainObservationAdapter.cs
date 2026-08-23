namespace Payaffe.Application.Payments;

public sealed class NoOpBlockchainObservationAdapter : IBlockchainObservationAdapter
{
    public Task StartWatchingAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<BlockchainObservation>>([]);
    }
}
