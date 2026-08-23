namespace Payaffe.Infrastructure.Payments;

public sealed class BlockchainObservationWorkerOptions
{
    public bool Enabled { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int MaxPaymentsPerPoll { get; set; } = 25;

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
