using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Payaffe.Sdk;

namespace EmbeddedShop;

/// <summary>
/// The second channel. Webhook Delivery is at-least-once, which also means it can be late or, in
/// a bad hour, lost; polling is what notices. One loop runs per order, not per open browser tab,
/// and it ends when the Payment reaches a terminal state.
/// </summary>
internal sealed class OrderReconciler(
    IPayaffeClientFactory clients,
    IOptions<ShopOptions> options,
    ILogger<OrderReconciler> logger) : BackgroundService
{
    private static readonly TimeSpan _initialRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maximumRestartDelay = TimeSpan.FromMinutes(1);

    private readonly Channel<ShopOrder> _queue = Channel.CreateUnbounded<ShopOrder>();
    private readonly ConcurrentDictionary<Guid, byte> _tracked = new();

    /// <summary>
    /// Starts reconciling an order unless a loop for it is already running, so a repeated or
    /// racing Currency Selection does not multiply the reads.
    /// </summary>
    public void Track(ShopOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (options.Value.ReconcileByPolling &&
            order.PaymentId is not null &&
            _tracked.TryAdd(order.OrderId, 0))
        {
            _queue.Writer.TryWrite(order);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (ShopOrder order in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            // Deliberately not awaited: one slow order must not hold up the next one. The task
            // owns its own failures and ends with the Payment.
            _ = ReconcileAsync(order, stoppingToken);
        }
    }

    private async Task ReconcileAsync(ShopOrder order, CancellationToken cancellationToken)
    {
        try
        {
            // The SDK already polls through transient failures. Whatever reaches this loop would
            // otherwise end a fire-and-forget task silently, so every failure is logged, and only
            // a refusal that repeating cannot change stops reconciling.
            TimeSpan restartDelay = _initialRestartDelay;
            while (true)
            {
                try
                {
                    PayaffeClient client = clients.CreateClient(order.Storefront);
                    await foreach (Payment payment in client.PollPaymentAsync(
                        order.PaymentId!.Value,
                        cancellationToken: cancellationToken))
                    {
                        order.Apply(payment, "polling");
                    }

                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (PayaffeApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
                {
                    logger.LogError(
                        exception,
                        "Reconciling order {OrderId} stopped: {Code}, correlation {CorrelationId}.",
                        order.OrderId,
                        exception.Code,
                        exception.CorrelationId);
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Reconciling order {OrderId} failed; restarting in {Delay}.",
                        order.OrderId,
                        restartDelay);
                }

                await Task.Delay(restartDelay, cancellationToken);
                restartDelay = restartDelay * 2 < _maximumRestartDelay
                    ? restartDelay * 2
                    : _maximumRestartDelay;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is shutting down. The order keeps its state and the next start
            // picks it up again from the order book.
        }
        finally
        {
            // A stopped loop can be started again by the next selection.
            _tracked.TryRemove(order.OrderId, out _);
        }
    }
}
