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
    private readonly Channel<ShopOrder> _queue = Channel.CreateUnbounded<ShopOrder>();

    public void Track(ShopOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (options.Value.ReconcileByPolling && order.PaymentId is not null)
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
            PayaffeClient client = clients.CreateClient(order.Storefront);
            await foreach (Payment payment in client.PollPaymentAsync(
                order.PaymentId!.Value,
                cancellationToken: cancellationToken))
            {
                order.Apply(payment, "polling");
            }
        }
        catch (OperationCanceledException)
        {
            // The application is shutting down. The order keeps its state and the next start
            // picks it up again from the order book.
        }
        catch (PayaffeApiException exception)
        {
            logger.LogError(
                exception,
                "Reconciling order {OrderId} stopped: {Code}, correlation {CorrelationId}.",
                order.OrderId,
                exception.Code,
                exception.CorrelationId);
        }
    }
}
