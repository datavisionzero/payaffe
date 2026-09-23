using Payaffe.Application.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class ConfiguredBlockchainObservationAdapter(
    IOptions<BlockchainObservationOptions> options,
    IBlockchainObservationAdapterResolver adapterResolver) : IBlockchainObservationAdapter
{
    public Task StartWatchingAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        return ResolveAdapter().StartWatchingAsync(target, cancellationToken);
    }

    public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        return ResolveAdapter().PollAsync(target, cancellationToken);
    }

    public Task<bool> IsObservationAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(BlockchainObservationOptions.IsObserving(options.Value.Mode));
    }

    private IBlockchainObservationAdapter ResolveAdapter()
    {
        return adapterResolver.Resolve(BlockchainObservationOptions.NormalizeMode(options.Value.Mode));
    }
}
