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
    /// How long one delivery request may take. The event lease has to outlast
    /// it by <see cref="LeaseMargin"/>, or a slow receiver lets a second worker
    /// claim the event mid-request and deliver it again.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Time the lease keeps beyond the request for resolving the secret,
    /// building the payload, and writing the outcome.
    /// </summary>
    public static readonly TimeSpan LeaseMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Host names, addresses and CIDR networks that Webhook Delivery may reach
    /// although they are not public (ADR 0036), comma separated.
    /// </summary>
    public string? AllowedPrivateTargets { get; set; }
}
