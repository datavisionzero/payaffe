using System.Net;
using System.Text;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Payments;

public sealed class CoinGeckoExchangeRateSourceTests
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fetches_coin_gecko_rate_persists_cache_and_reuses_fresh_entry()
    {
        var cache = new InMemoryRateCacheStore();
        var handler = new RecordingHandler(_ => JsonResponse(
            """{"bitcoin":{"eur":50000,"last_updated_at":""" +
            RequestedAt.ToUnixTimeSeconds() +
            "}}"));
        var source = CreateSource(handler, cache);

        var first = await source.GetRateLockQuoteAsync(
            "EUR",
            1999,
            "BTC",
            RequestedAt,
            CancellationToken.None);
        var second = await source.GetRateLockQuoteAsync(
            "EUR",
            1999,
            "BTC",
            RequestedAt.AddMinutes(1),
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal("coingecko", first.RateSource);
        Assert.Equal("50000", first.RateValue);
        Assert.Equal("0.0003998", first.ExpectedCryptoAmount);
        Assert.Equal(RequestedAt, first.ObservedAt);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount);
        var cached = Assert.Single(cache.Values);
        Assert.Equal("EUR", cached.FiatCurrency);
        Assert.Equal("BTC", cached.SupportedCurrency);
        Assert.Equal(RequestedAt, cached.FetchedAt);
    }

    [Fact]
    public async Task Falls_back_to_stale_cache_within_limit_when_provider_fails()
    {
        var cache = new InMemoryRateCacheStore();
        await cache.UpsertAsync(
            new CachedExchangeRate(
                "USD",
                "LTC",
                "coingecko",
                "100",
                RequestedAt.AddMinutes(-20),
                RequestedAt.AddMinutes(-10)),
            CancellationToken.None);
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var source = CreateSource(handler, cache);

        var quote = await source.GetRateLockQuoteAsync(
            "USD",
            2500,
            "LTC",
            RequestedAt,
            CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("coingecko:stale-cache", quote.RateSource);
        Assert.Equal("0.25", quote.ExpectedCryptoAmount);
        Assert.Equal(RequestedAt.AddMinutes(-20), quote.ObservedAt);
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>
    /// An upstream that keeps answering with a frozen price is fetched fresh
    /// but its rate is old. It must not become a Rate Lock, whether it comes
    /// from the request itself or from a recently fetched cache entry.
    /// </summary>
    [Fact]
    public async Task Does_not_lock_a_freshly_fetched_rate_whose_observation_is_older_than_the_maximum_stale_age()
    {
        var frozenAt = RequestedAt.AddHours(-2);
        var cache = new InMemoryRateCacheStore();
        var handler = new RecordingHandler(_ => JsonResponse(
            """{"bitcoin":{"eur":50000,"last_updated_at":""" + frozenAt.ToUnixTimeSeconds() + "}}"));
        var source = CreateSource(handler, cache);

        var fromRequest = await source.GetRateLockQuoteAsync("EUR", 1999, "BTC", RequestedAt, CancellationToken.None);
        var fromCache = await source.GetRateLockQuoteAsync("EUR", 1999, "BTC", RequestedAt.AddMinutes(1), CancellationToken.None);

        Assert.Null(fromRequest);
        Assert.Null(fromCache);
        Assert.Equal(frozenAt, Assert.Single(cache.Values).ObservedAt);
    }

    [Fact]
    public async Task Treats_a_rate_too_small_to_convert_as_unavailable()
    {
        var cache = new InMemoryRateCacheStore();
        var handler = new RecordingHandler(_ => JsonResponse(
            """{"ethereum":{"eur":0.0000001,"last_updated_at":""" + RequestedAt.ToUnixTimeSeconds() + "}}"));
        var source = CreateSource(handler, cache);

        var quote = await source.GetRateLockQuoteAsync("EUR", 100_000_000, "ETH", RequestedAt, CancellationToken.None);

        Assert.Null(quote);
    }

    [Fact]
    public async Task Rejects_cache_older_than_maximum_stale_age()
    {
        var cache = new InMemoryRateCacheStore();
        await cache.UpsertAsync(
            new CachedExchangeRate(
                "EUR",
                "ETH",
                "coingecko",
                "3000",
                RequestedAt.AddMinutes(-31),
                RequestedAt.AddMinutes(-10)),
            CancellationToken.None);
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var source = CreateSource(handler, cache);

        var quote = await source.GetRateLockQuoteAsync(
            "EUR",
            1999,
            "ETH",
            RequestedAt,
            CancellationToken.None);

        Assert.Null(quote);
    }

    [Fact]
    public async Task Rounds_expected_amount_up_to_native_currency_precision()
    {
        var cache = new InMemoryRateCacheStore();
        var handler = new RecordingHandler(_ => JsonResponse(
            """{"ethereum":{"eur":3000,"last_updated_at":""" +
            RequestedAt.ToUnixTimeSeconds() +
            "}}"));
        var source = CreateSource(handler, cache);

        var quote = await source.GetRateLockQuoteAsync(
            "EUR",
            1999,
            "ETH",
            RequestedAt,
            CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("0.006663333333333334", quote.ExpectedCryptoAmount);
    }

    [Fact]
    public async Task Sends_configured_api_key_without_exposing_it_in_cache()
    {
        const string apiKey = "coin-gecko-secret";
        var cache = new InMemoryRateCacheStore();
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(apiKey, Assert.Single(request.Headers.GetValues("x-cg-demo-api-key")));
            Assert.Contains("ids=bitcoin", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("vs_currencies=usd", request.RequestUri.Query, StringComparison.Ordinal);
            return JsonResponse(
                """{"bitcoin":{"usd":60000,"last_updated_at":""" +
                RequestedAt.ToUnixTimeSeconds() +
                "}}");
        });
        var source = CreateSource(
            handler,
            cache,
            new ExchangeRateOptions
            {
                ApiKeyReference = "configuration:ExchangeRates:ProviderSecrets:coingecko",
            },
            new FixedSecretResolver(apiKey));

        var quote = await source.GetRateLockQuoteAsync(
            "USD",
            12000,
            "BTC",
            RequestedAt,
            CancellationToken.None);

        Assert.NotNull(quote);
        Assert.DoesNotContain(apiKey, Assert.Single(cache.Values).RateValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refreshes_all_supported_rate_pairs_in_one_provider_request()
    {
        var cache = new InMemoryRateCacheStore();
        var handler = new RecordingHandler(request =>
        {
            Assert.Contains("ids=bitcoin,litecoin,ethereum", request.RequestUri!.Query);
            Assert.Contains("vs_currencies=eur,usd", request.RequestUri.Query);
            return JsonResponse(
                $$"""
                {
                  "bitcoin": {
                    "eur": 50000,
                    "usd": 60000,
                    "last_updated_at": {{RequestedAt.ToUnixTimeSeconds()}}
                  },
                  "litecoin": {
                    "eur": 100,
                    "usd": 120,
                    "last_updated_at": {{RequestedAt.ToUnixTimeSeconds()}}
                  },
                  "ethereum": {
                    "eur": 3000,
                    "usd": 3500,
                    "last_updated_at": {{RequestedAt.ToUnixTimeSeconds()}}
                  }
                }
                """);
        });
        var source = CreateSource(handler, cache);

        var result = await source.RefreshAsync(RequestedAt, CancellationToken.None);

        Assert.Equal(6, result.RequestedPairCount);
        Assert.Equal(6, result.RefreshedPairCount);
        Assert.Equal(6, cache.Values.Count);
        Assert.Equal(1, handler.RequestCount);
    }

    private static CoinGeckoExchangeRateSource CreateSource(
        HttpMessageHandler handler,
        IRateCacheStore cache,
        ExchangeRateOptions? options = null,
        IExchangeRateSecretResolver? secretResolver = null)
    {
        return new CoinGeckoExchangeRateSource(
            new HttpClient(handler),
            cache,
            secretResolver ?? new FixedSecretResolver(secret: null),
            Options.Create(options ?? new ExchangeRateOptions()),
            NullLogger<CoinGeckoExchangeRateSource>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class InMemoryRateCacheStore : IRateCacheStore
    {
        private readonly Dictionary<(string Fiat, string Crypto), CachedExchangeRate> _rates = [];

        public IReadOnlyCollection<CachedExchangeRate> Values => _rates.Values;

        public Task<CachedExchangeRate?> FindAsync(
            string fiatCurrency,
            string supportedCurrency,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _rates.TryGetValue((fiatCurrency, supportedCurrency), out var rate)
                    ? rate
                    : null);
        }

        public Task UpsertAsync(CachedExchangeRate exchangeRate, CancellationToken cancellationToken)
        {
            _rates[(exchangeRate.FiatCurrency, exchangeRate.SupportedCurrency)] = exchangeRate;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSecretResolver(string? secret) : IExchangeRateSecretResolver
    {
        public Task<string?> ResolveAsync(
            string secretReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(secret);
    }
}
