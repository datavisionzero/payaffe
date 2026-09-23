using System.Globalization;
using System.Text.Json;
using Payaffe.Application.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class CoinGeckoExchangeRateSource(
    HttpClient httpClient,
    IRateCacheStore rateCache,
    IExchangeRateSecretResolver secretResolver,
    IOptions<ExchangeRateOptions> options,
    ILogger<CoinGeckoExchangeRateSource> logger)
    : IExchangeRateSource, IRateCacheRefresher
{
    private static readonly string[] SupportedFiatCurrencies = ["EUR", "USD"];
    private static readonly IReadOnlyDictionary<string, string> CoinGeckoIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BTC"] = "bitcoin",
            ["LTC"] = "litecoin",
            ["ETH"] = "ethereum",
        };
    private readonly ExchangeRateOptions _options = options.Value;

    public async Task<RateLockQuote?> GetRateLockQuoteAsync(
        string fiatCurrency,
        long fiatAmountMinor,
        string supportedCurrency,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        var normalizedFiatCurrency = fiatCurrency.Trim().ToUpperInvariant();
        var normalizedSupportedCurrency = supportedCurrency.Trim().ToUpperInvariant();
        if (!_options.Enabled ||
            fiatAmountMinor <= 0 ||
            normalizedFiatCurrency is not ("EUR" or "USD") ||
            !CoinGeckoIds.TryGetValue(normalizedSupportedCurrency, out var coinGeckoId))
        {
            return null;
        }

        var cached = await rateCache.FindAsync(
            normalizedFiatCurrency,
            normalizedSupportedCurrency,
            cancellationToken);
        // A rate becomes a Rate Lock only while its own observation time is
        // within the maximum stale age, on every path: an upstream that keeps
        // answering with a frozen price is fetched fresh but is not fresh.
        if (cached is not null &&
            requestedAt - cached.FetchedAt <= _options.CacheInterval &&
            IsWithinMaxStaleAge(cached, requestedAt))
        {
            var freshQuote = CreateQuote(cached, fiatAmountMinor, isStale: false);
            if (freshQuote is not null)
            {
                return freshQuote;
            }
        }

        var refreshed = (await FetchManyAsync(
            [(normalizedSupportedCurrency, coinGeckoId)],
            [normalizedFiatCurrency],
            requestedAt,
            cancellationToken)).SingleOrDefault();
        if (refreshed is not null)
        {
            await rateCache.UpsertAsync(refreshed, cancellationToken);
            if (IsWithinMaxStaleAge(refreshed, requestedAt))
            {
                return CreateQuote(refreshed, fiatAmountMinor, isStale: false);
            }
        }

        if (cached is not null && IsWithinMaxStaleAge(cached, requestedAt))
        {
            return CreateQuote(cached, fiatAmountMinor, isStale: true);
        }

        return null;
    }

    public async Task<bool> IsRateAvailableAsync(
        string fiatCurrency,
        string supportedCurrency,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        var normalizedFiatCurrency = fiatCurrency.Trim().ToUpperInvariant();
        var normalizedSupportedCurrency = supportedCurrency.Trim().ToUpperInvariant();
        if (!_options.Enabled ||
            normalizedFiatCurrency is not ("EUR" or "USD") ||
            !CoinGeckoIds.ContainsKey(normalizedSupportedCurrency))
        {
            return false;
        }

        var cached = await rateCache.FindAsync(
            normalizedFiatCurrency,
            normalizedSupportedCurrency,
            cancellationToken);
        return cached is not null &&
               checkedAt - cached.ObservedAt <= _options.MaxStaleAge &&
               decimal.TryParse(
                   cached.RateValue,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out var rate) &&
               rate > 0;
    }

    public async Task<RateCacheRefreshResult> RefreshAsync(
        DateTimeOffset refreshedAt,
        CancellationToken cancellationToken)
    {
        const int requestedPairCount = 6;
        if (!_options.Enabled)
        {
            return new RateCacheRefreshResult(requestedPairCount, RefreshedPairCount: 0);
        }

        var refreshed = await FetchManyAsync(
            CoinGeckoIds.Select(pair => (pair.Key, pair.Value)).ToArray(),
            SupportedFiatCurrencies,
            refreshedAt,
            cancellationToken);
        foreach (var rate in refreshed)
        {
            await rateCache.UpsertAsync(rate, cancellationToken);
        }

        return new RateCacheRefreshResult(requestedPairCount, refreshed.Count);
    }

    private async Task<IReadOnlyList<CachedExchangeRate>> FetchManyAsync(
        IReadOnlyCollection<(string SupportedCurrency, string CoinGeckoId)> currencies,
        IReadOnlyCollection<string> fiatCurrencies,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var ids = string.Join(',', currencies.Select(currency => currency.CoinGeckoId));
            var fiatQueryNames = string.Join(
                ',',
                fiatCurrencies.Select(currency => currency.ToLowerInvariant()));
            var uri = new Uri(
                _options.BaseUrl,
                $"/api/v3/simple/price?ids={ids}" +
                $"&vs_currencies={fiatQueryNames}" +
                "&include_last_updated_at=true&precision=full");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(_options.ApiKeyReference))
            {
                var apiKey = await secretResolver.ResolveAsync(
                    _options.ApiKeyReference,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    logger.LogWarning(
                        "CoinGecko API key reference could not be resolved; cached rate fallback will be evaluated.");
                    return [];
                }

                request.Headers.Add("x-cg-demo-api-key", apiKey);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "CoinGecko rate request failed with HTTP status {StatusCode}.",
                    (int)response.StatusCode);
                return [];
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token),
                cancellationToken: timeout.Token);
            var result = new List<CachedExchangeRate>();
            foreach (var currency in currencies)
            {
                if (!document.RootElement.TryGetProperty(currency.CoinGeckoId, out var coin))
                {
                    continue;
                }

                var observedAt = coin.TryGetProperty("last_updated_at", out var lastUpdated) &&
                                 lastUpdated.TryGetInt64(out var unixSeconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                    : requestedAt;
                foreach (var fiatCurrency in fiatCurrencies)
                {
                    var fiatQueryName = fiatCurrency.ToLowerInvariant();
                    if (!coin.TryGetProperty(fiatQueryName, out var rateElement) ||
                        !TryReadPositiveDecimal(rateElement, out var rate))
                    {
                        continue;
                    }

                    result.Add(new CachedExchangeRate(
                        fiatCurrency,
                        currency.SupportedCurrency,
                        "coingecko",
                        FormatDecimal(rate),
                        observedAt,
                        requestedAt));
                }
            }

            if (result.Count == 0)
            {
                logger.LogWarning("CoinGecko rate response did not contain any valid requested rates.");
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("CoinGecko rate request timed out.");
            return [];
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "CoinGecko rate request failed.");
            return [];
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "CoinGecko rate response was invalid JSON.");
            return [];
        }
    }

    private bool IsWithinMaxStaleAge(CachedExchangeRate rate, DateTimeOffset requestedAt) =>
        requestedAt - rate.ObservedAt <= _options.MaxStaleAge;

    private static RateLockQuote? CreateQuote(
        CachedExchangeRate rate,
        long fiatAmountMinor,
        bool isStale)
    {
        if (!decimal.TryParse(
                rate.RateValue,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var rateValue) ||
            rateValue <= 0)
        {
            return null;
        }

        var fiatAmount = fiatAmountMinor / 100m;
        var precision = rate.SupportedCurrency == "ETH" ? 18 : 8;
        decimal expectedAmount;
        try
        {
            expectedAmount = RoundUp(fiatAmount / rateValue, precision);
        }
        catch (OverflowException)
        {
            // Only an implausibly small rate gets here; it is no usable rate.
            return null;
        }

        return new RateLockQuote(
            rate.SupportedCurrency,
            isStale ? $"{rate.RateSource}:stale-cache" : rate.RateSource,
            rate.RateValue,
            FormatDecimal(expectedAmount),
            rate.ObservedAt);
    }

    internal static decimal RoundUp(decimal value, int decimalPlaces)
    {
        var factor = decimalPlaces == 18
            ? 1_000_000_000_000_000_000m
            : 100_000_000m;
        return decimal.Ceiling(value * factor) / factor;
    }

    private static bool TryReadPositiveDecimal(JsonElement element, out decimal value)
    {
        value = 0;
        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(
                element.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value),
            _ => false,
        };
        return parsed && value > 0;
    }

    internal static string FormatDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
