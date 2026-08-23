namespace Payaffe.Application.Payments;

public interface IExchangeRateSecretResolver
{
    Task<string?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken);
}
