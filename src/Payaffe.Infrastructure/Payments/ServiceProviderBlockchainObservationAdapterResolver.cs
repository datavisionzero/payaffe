using Payaffe.Application.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Infrastructure.Payments;

public sealed class ServiceProviderBlockchainObservationAdapterResolver(IServiceProvider serviceProvider)
    : IBlockchainObservationAdapterResolver
{
    public IBlockchainObservationAdapter Resolve(string mode)
    {
        return mode switch
        {
            "none" => serviceProvider.GetRequiredService<NoOpBlockchainObservationAdapter>(),
            "blockchair" => serviceProvider.GetRequiredService<BlockchairBlockchainObservationAdapter>(),
            "nownodes" => serviceProvider.GetRequiredService<NownodesBlockchainObservationAdapter>(),
            _ => throw new InvalidOperationException(
                $"No Blockchain Observation adapter is registered for mode '{mode}'."),
        };
    }
}
