namespace Payaffe.Application.Payments;

public sealed class UnavailableExchangeRateSecretResolver : IExchangeRateSecretResolver
{
    public Task<string?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
