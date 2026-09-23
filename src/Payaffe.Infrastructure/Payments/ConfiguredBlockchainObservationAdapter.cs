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

    /// <summary>
    /// A selected mode is not enough: the provider has to be able to poll, so
    /// its own readiness (such as a resolvable API key) is checked before a
    /// Payment Address is assigned.
    /// </summary>
    public async Task<bool> IsObservationAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        return BlockchainObservationOptions.IsObserving(options.Value.Mode) &&
               await ResolveAdapter().IsObservationAvailableAsync(supportedCurrency, cancellationToken);
    }

    private IBlockchainObservationAdapter ResolveAdapter()
    {
        return adapterResolver.Resolve(BlockchainObservationOptions.NormalizeMode(options.Value.Mode));
    }
}
