using Payaffe.Application.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

/// <summary>
/// The Exchange Rate Source of a Test Mode installation (ADR 0033): fixed
/// rates from configuration, no provider and no network.
/// </summary>
/// <remarks>
/// Fixed rather than live so that a test integration gets the same crypto
/// amount for the same Fiat Amount every time it runs.
/// </remarks>
public sealed class SimulatedExchangeRateSource(IOptions<SimulatedExchangeRateOptions> options)
    : IExchangeRateSource, IRateCacheRefresher
{
    public const string SourceName = "simulated";

    private readonly SimulatedExchangeRateOptions _options = options.Value;

    public Task<RateLockQuote?> GetRateLockQuoteAsync(
        string fiatCurrency,
        long fiatAmountMinor,
        string supportedCurrency,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        var normalizedSupportedCurrency = supportedCurrency.Trim().ToUpperInvariant();
        var rate = _options.Find(fiatCurrency.Trim().ToUpperInvariant(), normalizedSupportedCurrency);
        if (rate is null || fiatAmountMinor <= 0)
        {
            return Task.FromResult<RateLockQuote?>(null);
        }

        var precision = normalizedSupportedCurrency == "ETH" ? 18 : 8;
        var expectedAmount = CoinGeckoExchangeRateSource.RoundUp(fiatAmountMinor / 100m / rate.Value, precision);
        return Task.FromResult<RateLockQuote?>(new RateLockQuote(
            normalizedSupportedCurrency,
            SourceName,
            CoinGeckoExchangeRateSource.FormatDecimal(rate.Value),
            CoinGeckoExchangeRateSource.FormatDecimal(expectedAmount),
            requestedAt));
    }

    public Task<bool> IsRateAvailableAsync(
        string fiatCurrency,
        string supportedCurrency,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(_options.Find(
            fiatCurrency.Trim().ToUpperInvariant(),
            supportedCurrency.Trim().ToUpperInvariant()) is not null);

    /// <summary>Nothing to refresh: the rates do not move.</summary>
    public Task<RateCacheRefreshResult> RefreshAsync(
        DateTimeOffset refreshedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(new RateCacheRefreshResult(RequestedPairCount: 6, RefreshedPairCount: 6));
}

/// <summary>
/// The fixed rates of Test Mode, in fiat per coin. The defaults are round
/// numbers near real prices, so amounts look plausible in a test and are easy
/// to check by hand.
/// </summary>
public sealed class SimulatedExchangeRateOptions
{
    public const string SectionName = "ExchangeRates:Simulated";

    public decimal BtcEur { get; set; } = 50_000m;

    public decimal BtcUsd { get; set; } = 55_000m;

    public decimal LtcEur { get; set; } = 80m;

    public decimal LtcUsd { get; set; } = 90m;

    public decimal EthEur { get; set; } = 2_500m;

    public decimal EthUsd { get; set; } = 2_750m;

    internal decimal? Find(string fiatCurrency, string supportedCurrency)
    {
        var rate = (supportedCurrency, fiatCurrency) switch
        {
            ("BTC", "EUR") => BtcEur,
            ("BTC", "USD") => BtcUsd,
            ("LTC", "EUR") => LtcEur,
            ("LTC", "USD") => LtcUsd,
            ("ETH", "EUR") => EthEur,
            ("ETH", "USD") => EthUsd,
            _ => 0m,
        };
        return rate > 0 ? rate : null;
    }
}

public sealed class SimulatedExchangeRateOptionsValidator : IValidateOptions<SimulatedExchangeRateOptions>
{
    public ValidateOptionsResult Validate(string? name, SimulatedExchangeRateOptions options)
    {
        var rates = new[]
        {
            options.BtcEur, options.BtcUsd, options.LtcEur, options.LtcUsd, options.EthEur, options.EthUsd,
        };
        return rates.All(rate => rate > 0)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Every rate in {SimulatedExchangeRateOptions.SectionName} must be greater than zero.");
    }
}
