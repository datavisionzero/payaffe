namespace Payaffe.Infrastructure;

/// <summary>
/// The `User-Agent` every outbound provider request identifies itself with.
/// </summary>
/// <remarks>
/// This is not cosmetic. `HttpClient` sends no `User-Agent` by default, and
/// CoinGecko answers such requests with HTTP 403. Without this header the Rate
/// Cache never fills and Currency Selection fails closed with
/// `exchange_rate.unavailable` on every Payment. Blockchain data providers
/// apply comparable anti-abuse rules, so every provider client sends it.
/// </remarks>
public static class ProductUserAgent
{
    public static string Value { get; } = $"payaffe/{ProductVersion.Value}";
}
