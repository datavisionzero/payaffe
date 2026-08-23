namespace Payaffe.Application.Payments;

public sealed class UnavailableExchangeRateSource : IExchangeRateSource
{
    public Task<RateLockQuote?> GetRateLockQuoteAsync(
        string fiatCurrency,
        long fiatAmountMinor,
        string supportedCurrency,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<RateLockQuote?>(null);
    }

    public Task<bool> IsRateAvailableAsync(
        string fiatCurrency,
        string supportedCurrency,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
