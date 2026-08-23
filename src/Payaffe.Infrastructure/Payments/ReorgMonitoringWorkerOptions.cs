namespace Payaffe.Infrastructure.Payments;

public sealed class ReorgMonitoringWorkerOptions
{
    public bool Enabled { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxTransactionsPerPoll { get; set; } = 25;

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
