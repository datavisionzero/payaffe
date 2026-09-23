namespace Payaffe.Infrastructure.Webhooks;

public sealed class WebhookDeliveryOptions
{
    public bool Enabled { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    public double RetryBackoffMultiplier { get; set; } = 2.0;

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(30);

    public double RetryJitterRatio { get; set; } = 0.2;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    public int MaxEventsPerPoll { get; set; } = 25;

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Host names, addresses and CIDR networks that Webhook Delivery may reach
    /// although they are not public (ADR 0036), comma separated.
    /// </summary>
    public string? AllowedPrivateTargets { get; set; }
}
