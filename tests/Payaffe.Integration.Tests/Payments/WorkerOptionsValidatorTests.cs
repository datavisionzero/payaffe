using Payaffe.Application.Payments;
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
        Assert.True(new PaymentApplicationOptionsValidator()
            .Validate(null, new PaymentApplicationOptions()).Succeeded);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24 * 31)]
    public void Rejects_an_observed_confirmation_wait_outside_zero_to_thirty_days(int hours)
    {
        var result = new PaymentApplicationOptionsValidator().Validate(
            null,
            new PaymentApplicationOptions { ObservedConfirmationWait = TimeSpan.FromHours(hours) });

        Assert.False(result.Succeeded);
        Assert.Contains("Payments:ObservedConfirmationWait must be between zero and 30 days.", result.Failures!);
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

    [Fact]
    public void Rejects_a_webhook_lease_that_a_slow_receiver_can_outlast()
    {
        var result = new WebhookDeliveryOptionsValidator().Validate(
            null,
            new WebhookDeliveryOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                LeaseDuration = TimeSpan.FromSeconds(45),
            });

        Assert.False(result.Succeeded);
        Assert.Contains(
            "Webhooks:Delivery:LeaseDuration must exceed RequestTimeout by at least 30 seconds.",
            result.Failures!);
        Assert.True(new WebhookDeliveryOptionsValidator().Validate(
            null,
            new WebhookDeliveryOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                LeaseDuration = TimeSpan.FromSeconds(60),
            }).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Rejects_a_webhook_request_timeout_out_of_range(int seconds)
    {
        var result = new WebhookDeliveryOptionsValidator().Validate(
            null,
            new WebhookDeliveryOptions { RequestTimeout = TimeSpan.FromSeconds(seconds) });

        Assert.False(result.Succeeded);
        Assert.Contains(
            "Webhooks:Delivery:RequestTimeout must be between one second and five minutes.",
            result.Failures!);
    }
}
