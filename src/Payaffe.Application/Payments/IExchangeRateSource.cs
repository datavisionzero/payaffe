namespace Payaffe.Application.Payments;

public interface IExchangeRateSource
{
    Task<RateLockQuote?> GetRateLockQuoteAsync(
        string fiatCurrency,
        long fiatAmountMinor,
        string supportedCurrency,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken);

    Task<bool> IsRateAvailableAsync(
        string fiatCurrency,
        string supportedCurrency,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

public sealed record RateLockQuote(
    string SupportedCurrency,
    string RateSource,
    string RateValue,
    string ExpectedCryptoAmount,
    DateTimeOffset ObservedAt);

public interface IRateCacheRefresher
{
    Task<RateCacheRefreshResult> RefreshAsync(
        DateTimeOffset refreshedAt,
        CancellationToken cancellationToken);
}

public sealed record RateCacheRefreshResult(int RequestedPairCount, int RefreshedPairCount);
