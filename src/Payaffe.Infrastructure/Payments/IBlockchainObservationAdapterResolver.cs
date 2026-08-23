using Payaffe.Application.Payments;

namespace Payaffe.Infrastructure.Payments;

public interface IBlockchainObservationAdapterResolver
{
    IBlockchainObservationAdapter Resolve(string mode);
}
