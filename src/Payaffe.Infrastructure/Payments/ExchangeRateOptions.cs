namespace Payaffe.Infrastructure.Payments;

public sealed class ExchangeRateOptions
{
    public bool Enabled { get; set; } = true;

    public Uri BaseUrl { get; set; } = new("https://api.coingecko.com");

    public string? ApiKeyReference { get; set; }

    public TimeSpan CacheInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaxStaleAge { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
