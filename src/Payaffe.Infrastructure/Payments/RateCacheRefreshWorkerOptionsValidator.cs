using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class RateCacheRefreshWorkerOptionsValidator
    : IValidateOptions<RateCacheRefreshWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, RateCacheRefreshWorkerOptions options)
    {
        return !options.Enabled ||
               options.RefreshInterval >= TimeSpan.FromSeconds(30)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "ExchangeRates:RefreshWorker:RefreshInterval must be at least 30 seconds.");
    }
}
