using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Webhooks;

namespace Payaffe.Integration.Tests.Payments;

public sealed class WorkerOptionsValidatorTests
{
    [Fact]
    public void Rejects_invalid_payment_worker_intervals_and_batches()
    {
        Assert.False(new PaymentLifecycleWorkerOptionsValidator().Validate(
            null,
            new PaymentLifecycleWorkerOptions
            {
                PollInterval = TimeSpan.Zero,
                ExpirationBatchSize = 0,
            }).Succeeded);
        Assert.False(new BlockchainObservationWorkerOptionsValidator().Validate(
            null,
            new BlockchainObservationWorkerOptions
            {
                PollInterval = TimeSpan.Zero,
                MaxPaymentsPerPoll = 0,
            }).Succeeded);
        Assert.False(new ReorgMonitoringWorkerOptionsValidator().Validate(
            null,
            new ReorgMonitoringWorkerOptions
            {
                PollInterval = TimeSpan.Zero,
                MaxTransactionsPerPoll = 0,
            }).Succeeded);
    }

    [Fact]
    public void Rejects_a_lease_that_cannot_exclude_a_second_instance()
    {
        var result = new PaymentLifecycleWorkerOptionsValidator().Validate(
            null,
            new PaymentLifecycleWorkerOptions
            {
                PollInterval = TimeSpan.FromMinutes(1),
                ExpirationBatchSize = 100,
                LeaseDuration = TimeSpan.Zero,
            });

        Assert.False(result.Succeeded);
        Assert.Contains(
            "Payments:LifecycleWorker:LeaseDuration must be at least one second.",
            result.Failures!);
    }

    [Fact]
    public void Rejects_a_lease_that_delays_recovery_beyond_an_hour()
    {
        var result = new BlockchainObservationWorkerOptionsValidator().Validate(
            null,
            new BlockchainObservationWorkerOptions
            {
                PollInterval = TimeSpan.FromMinutes(1),
                MaxPaymentsPerPoll = 25,
                LeaseDuration = TimeSpan.FromHours(2),
            });

        Assert.False(result.Succeeded);
        Assert.Contains(
            "Payments:ObservationWorker:LeaseDuration must not exceed one hour.",
            result.Failures!);
    }

    [Fact]
    public void Accepts_the_shipped_worker_defaults()
    {
        Assert.True(new PaymentLifecycleWorkerOptionsValidator()
            .Validate(null, new PaymentLifecycleWorkerOptions()).Succeeded);
        Assert.True(new BlockchainObservationWorkerOptionsValidator()
            .Validate(null, new BlockchainObservationWorkerOptions()).Succeeded);
        Assert.True(new ReorgMonitoringWorkerOptionsValidator()
            .Validate(null, new ReorgMonitoringWorkerOptions()).Succeeded);
        Assert.True(new WebhookDeliveryOptionsValidator()
            .Validate(null, new WebhookDeliveryOptions()).Succeeded);
    }

    [Fact]
    public void Rejects_invalid_webhook_delivery_retry_configuration()
    {
        var result = new WebhookDeliveryOptionsValidator().Validate(
            null,
            new WebhookDeliveryOptions
            {
                MaxAttempts = 0,
                RetryDelay = TimeSpan.FromMinutes(2),
                MaxRetryDelay = TimeSpan.FromMinutes(1),
                RetryBackoffMultiplier = 0,
                RetryJitterRatio = 2,
                PollInterval = TimeSpan.Zero,
                MaxEventsPerPoll = 0,
            });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Rejects_an_allowed_private_target_that_is_not_a_host_address_or_network()
    {
        var result = new WebhookDeliveryOptionsValidator().Validate(
            null,
            new WebhookDeliveryOptions { AllowedPrivateTargets = "shop.internal, 10.0.0.0/40" });

        Assert.False(result.Succeeded);
        Assert.Contains("'10.0.0.0/40'", result.FailureMessage, StringComparison.Ordinal);
        Assert.True(new WebhookDeliveryOptionsValidator()
            .Validate(null, new WebhookDeliveryOptions { AllowedPrivateTargets = "shop.internal, 10.0.0.0/8" })
            .Succeeded);
    }
}
