using Payaffe.Application.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class ObservationHealthTrackingAdapter(
    ConfiguredBlockchainObservationAdapter inner,
    IObservationHealthStore healthStore,
    IClock clock,
    IOptions<BlockchainObservationOptions> options)
    : IBlockchainObservationAdapter
{
    public async Task StartWatchingAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        await TrackAsync(
            target.SupportedCurrency,
            () => inner.StartWatchingAsync(target, cancellationToken),
            cancellationToken);
    }

    public async Task<IReadOnlyList<BlockchainObservation>> PollAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BlockchainObservation> result = [];
        await TrackAsync(
            target.SupportedCurrency,
            async () => result = await inner.PollAsync(target, cancellationToken),
            cancellationToken);
        return result;
    }

    public async Task<bool> IsObservationAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        if (!await inner.IsObservationAvailableAsync(supportedCurrency, cancellationToken))
        {
            return false;
        }

        return await healthStore.IsAvailableAsync(supportedCurrency, cancellationToken) ?? true;
    }

    private async Task TrackAsync(
        string supportedCurrency,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        var providerName = BlockchainObservationOptions.NormalizeMode(options.Value.Mode);
        try
        {
            await operation();
            await healthStore.RecordSuccessAsync(
                supportedCurrency,
                providerName,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            await healthStore.RecordFailureAsync(
                supportedCurrency,
                providerName,
                ToSafeErrorCode(exception),
                clock.UtcNow,
                cancellationToken);
            throw;
        }
    }

    private static string ToSafeErrorCode(Exception exception) =>
        exception switch
        {
            HttpRequestException => "observation_provider.http_failure",
            TaskCanceledException => "observation_provider.timeout",
            _ => "observation_provider.failure",
        };
}
