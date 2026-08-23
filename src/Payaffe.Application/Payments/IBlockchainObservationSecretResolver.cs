namespace Payaffe.Application.Payments;

public interface IBlockchainObservationSecretResolver
{
    Task<string?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken);
}
