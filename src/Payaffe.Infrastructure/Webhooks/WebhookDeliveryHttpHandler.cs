using System.Net;
using System.Net.Sockets;
using Payaffe.Application.Webhooks;

namespace Payaffe.Infrastructure.Webhooks;

/// <summary>
/// The connection Webhook Delivery makes (ADR 0036). The target is checked
/// where the socket is opened, against the addresses the name resolved to at
/// that moment, so a name that resolves to a public address when an Endpoint
/// is saved and to an internal one at delivery is still refused.
/// </summary>
public static class WebhookDeliveryHttpHandler
{
    public static SocketsHttpHandler Create(WebhookTargetPolicy policy) =>
        new()
        {
            // A 3xx is a terminal answer in the contract. Following it would
            // re-post the signed body to wherever the receiver points.
            AllowAutoRedirect = false,
            // A proxy would make the connection this handler checks the
            // proxy's, and the target a request the proxy makes on its own.
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
                ConnectAsync(policy, context.DnsEndPoint, cancellationToken),
        };

    internal static async ValueTask<Stream> ConnectAsync(
        WebhookTargetPolicy policy,
        DnsEndPoint endPoint,
        CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);
        var allowed = policy.AllowsHost(endPoint.Host)
            ? addresses
            : addresses.Where(policy.AllowsAddress).ToArray();
        if (allowed.Length == 0)
        {
            throw new WebhookTargetRefusedException();
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, endPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>
/// The target resolved only to addresses Webhook Delivery may not reach. The
/// message carries no address, because it can end up in a log line.
/// </summary>
public sealed class WebhookTargetRefusedException()
    : Exception("The Webhook Endpoint resolves only to addresses that are not public and not allowlisted.");
