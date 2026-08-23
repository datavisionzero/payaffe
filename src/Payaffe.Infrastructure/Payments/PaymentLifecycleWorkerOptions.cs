namespace Payaffe.Infrastructure.Payments;

public sealed class PaymentLifecycleWorkerOptions
{
    public bool Enabled { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int ExpirationBatchSize { get; set; } = 100;

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
