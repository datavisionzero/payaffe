namespace Payaffe.Application.Payments;

public interface IRateCacheStore
{
    Task<CachedExchangeRate?> FindAsync(
        string fiatCurrency,
        string supportedCurrency,
        CancellationToken cancellationToken);

    Task UpsertAsync(
        CachedExchangeRate exchangeRate,
        CancellationToken cancellationToken);
}

public sealed record CachedExchangeRate(
    string FiatCurrency,
    string SupportedCurrency,
    string RateSource,
    string RateValue,
    DateTimeOffset ObservedAt,
    DateTimeOffset FetchedAt);
