using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class PaymentLifecycleWorkerOptionsValidator
    : IValidateOptions<PaymentLifecycleWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentLifecycleWorkerOptions options) =>
        ValidateWorker(
            options.PollInterval,
            options.ExpirationBatchSize,
            options.LeaseDuration,
            "Payments:LifecycleWorker");

    internal static ValidateOptionsResult ValidateWorker(
        TimeSpan pollInterval,
        int batchSize,
        TimeSpan leaseDuration,
        string section)
    {
        var failures = new List<string>();
        if (pollInterval < TimeSpan.FromSeconds(1))
        {
            failures.Add($"{section}:PollInterval must be at least one second.");
        }

        if (batchSize is < 1 or > 1000)
        {
            failures.Add($"{section} batch size must be between 1 and 1000.");
        }

        failures.AddRange(ValidateLeaseDuration(leaseDuration, section));

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// A lease is acquired for one run and released when that run ends, so it has
    /// to outlive a single batch rather than the poll interval. Too short a lease
    /// lets a second host instance take over mid-run, which is the concurrent
    /// processing the lease exists to prevent; too long a lease delays recovery
    /// after a crashed worker. Both ends are therefore bounded.
    /// </summary>
    internal static IEnumerable<string> ValidateLeaseDuration(TimeSpan leaseDuration, string section)
    {
        if (leaseDuration < TimeSpan.FromSeconds(1))
        {
            yield return $"{section}:LeaseDuration must be at least one second.";
            yield break;
        }

        if (leaseDuration > TimeSpan.FromHours(1))
        {
            yield return $"{section}:LeaseDuration must not exceed one hour.";
        }
    }
}

public sealed class BlockchainObservationWorkerOptionsValidator
    : IValidateOptions<BlockchainObservationWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, BlockchainObservationWorkerOptions options) =>
        PaymentLifecycleWorkerOptionsValidator.ValidateWorker(
            options.PollInterval,
            options.MaxPaymentsPerPoll,
            options.LeaseDuration,
            "Payments:ObservationWorker");
}

public sealed class ReorgMonitoringWorkerOptionsValidator
    : IValidateOptions<ReorgMonitoringWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, ReorgMonitoringWorkerOptions options) =>
        PaymentLifecycleWorkerOptionsValidator.ValidateWorker(
            options.PollInterval,
            options.MaxTransactionsPerPoll,
            options.LeaseDuration,
            "Payments:ReorgMonitoringWorker");
}

public sealed class WebhookDeliveryOptionsValidator : IValidateOptions<WebhookDeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, WebhookDeliveryOptions options)
    {
        var failures = new List<string>();
        if (options.MaxAttempts is < 1 or > 100)
        {
            failures.Add("Webhooks:Delivery:MaxAttempts must be between 1 and 100.");
        }

        if (options.RetryDelay <= TimeSpan.Zero ||
            options.MaxRetryDelay < options.RetryDelay ||
            options.PollInterval < TimeSpan.FromSeconds(1))
        {
            failures.Add("Webhooks:Delivery retry and poll intervals are invalid.");
        }

        if (options.RetryBackoffMultiplier is < 1 or > 100 ||
            options.RetryJitterRatio is < 0 or > 1)
        {
            failures.Add("Webhooks:Delivery backoff or jitter is invalid.");
        }

        if (options.MaxEventsPerPoll is < 1 or > 1000)
        {
            failures.Add("Webhooks:Delivery:MaxEventsPerPoll must be between 1 and 1000.");
        }

        failures.AddRange(PaymentLifecycleWorkerOptionsValidator.ValidateLeaseDuration(
            options.LeaseDuration,
            "Webhooks:Delivery"));
        failures.AddRange(WebhookTargetPolicy.Validate(options.AllowedPrivateTargets));

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
