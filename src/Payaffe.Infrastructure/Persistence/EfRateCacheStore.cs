using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfRateCacheStore(PayaffeDbContext dbContext) : IRateCacheStore
{
    public async Task<CachedExchangeRate?> FindAsync(
        string fiatCurrency,
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        return await dbContext.RateCache
            .AsNoTracking()
            .Where(rate =>
                rate.FiatCurrency == fiatCurrency &&
                rate.SupportedCurrency == supportedCurrency)
            .Select(rate => new CachedExchangeRate(
                rate.FiatCurrency,
                rate.SupportedCurrency,
                rate.RateSource,
                rate.RateValue,
                rate.ObservedAt,
                rate.FetchedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        CachedExchangeRate exchangeRate,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into app.rate_cache (
                    fiat_currency,
                    supported_currency,
                    rate_source,
                    rate_value,
                    observed_at,
                    fetched_at,
                    version)
                values (
                    {exchangeRate.FiatCurrency},
                    {exchangeRate.SupportedCurrency},
                    {exchangeRate.RateSource},
                    {exchangeRate.RateValue},
                    {exchangeRate.ObservedAt},
                    {exchangeRate.FetchedAt},
                    1)
                on conflict (fiat_currency, supported_currency)
                do update set
                    rate_source = excluded.rate_source,
                    rate_value = excluded.rate_value,
                    observed_at = excluded.observed_at,
                    fetched_at = excluded.fetched_at,
                    version = app.rate_cache.version + 1
                """,
                cancellationToken);
            return;
        }

        var existing = await dbContext.RateCache.SingleOrDefaultAsync(
            rate =>
                rate.FiatCurrency == exchangeRate.FiatCurrency &&
                rate.SupportedCurrency == exchangeRate.SupportedCurrency,
            cancellationToken);
        if (existing is null)
        {
            dbContext.RateCache.Add(new RateCacheRecord
            {
                FiatCurrency = exchangeRate.FiatCurrency,
                SupportedCurrency = exchangeRate.SupportedCurrency,
                RateSource = exchangeRate.RateSource,
                RateValue = exchangeRate.RateValue,
                ObservedAt = exchangeRate.ObservedAt,
                FetchedAt = exchangeRate.FetchedAt,
            });
        }
        else
        {
            existing.RateSource = exchangeRate.RateSource;
            existing.RateValue = exchangeRate.RateValue;
            existing.ObservedAt = exchangeRate.ObservedAt;
            existing.FetchedAt = exchangeRate.FetchedAt;
            existing.Version++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
