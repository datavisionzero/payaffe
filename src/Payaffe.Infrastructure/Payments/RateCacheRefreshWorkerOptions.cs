namespace Payaffe.Infrastructure.Payments;

public sealed class RateCacheRefreshWorkerOptions
{
    public bool Enabled { get; set; } = true;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
