using System.Net;
using System.Net.Sockets;

namespace Payaffe.Application.Webhooks;

/// <summary>
/// Which addresses Webhook Delivery may connect to (ADR 0036). A Webhook
/// Endpoint URL is chosen by whoever holds an Admin session, and the delivery
/// worker posts a signed body to it from inside the operator's network, so
/// only public addresses are reached unless the installation names a private
/// target it trusts.
/// </summary>
public sealed class WebhookTargetPolicy
{
    public const string ConfigurationKey = "Webhooks:Delivery:AllowedPrivateTargets";

    /// <summary>The safe error code a refused delivery records.</summary>
    public const string RefusedErrorCode = "webhook_target.not_public";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    private static readonly IPNetwork[] NonPublicNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/96"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fec0::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    private readonly HashSet<string> _hosts;
    private readonly IReadOnlyList<IPNetwork> _networks;

    private WebhookTargetPolicy(HashSet<string> hosts, IReadOnlyList<IPNetwork> networks)
    {
        _hosts = hosts;
        _networks = networks;
    }

    public static WebhookTargetPolicy PublicOnly { get; } = new([], []);

    /// <summary>
    /// Reads host names, addresses and CIDR networks. An entry that is none of
    /// them is refused rather than skipped: an allowlist that silently lost an
    /// entry fails every delivery to that target, and an entry that silently
    /// became something wider would reach more than was meant.
    /// </summary>
    public static WebhookTargetPolicy Parse(string? configured)
    {
        var errors = Validate(configured);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var networks = new List<IPNetwork>();
        foreach (var entry in Split(configured))
        {
            if (TryParseNetwork(entry, out var network))
            {
                networks.Add(network);
            }
            else
            {
                hosts.Add(NormalizeHost(entry));
            }
        }

        return new WebhookTargetPolicy(hosts, networks);
    }

    public static IReadOnlyList<string> Validate(string? configured)
    {
        var errors = new List<string>();
        foreach (var entry in Split(configured))
        {
            if (entry.Contains('/', StringComparison.Ordinal))
            {
                if (!IPNetwork.TryParse(entry, out _))
                {
                    errors.Add($"{ConfigurationKey} entry '{entry}' was rejected: it is not a CIDR network.");
                }

                continue;
            }

            if (!IPAddress.TryParse(entry, out _) &&
                Uri.CheckHostName(NormalizeHost(entry)) != UriHostNameType.Dns)
            {
                errors.Add($"{ConfigurationKey} entry '{entry}' was rejected: it is not a host name, an IP address or a CIDR network.");
            }
        }

        return errors;
    }

    /// <summary>True when the host name itself was allowlisted.</summary>
    public bool AllowsHost(string host) => _hosts.Contains(NormalizeHost(host));

    /// <summary>True when the address is public or falls under an allowlisted network.</summary>
    public bool AllowsAddress(IPAddress address)
    {
        var candidate = Normalize(address);
        return IsPublic(candidate) || _networks.Any(network => network.Contains(candidate));
    }

    /// <summary>
    /// What an Admin can be told when an Endpoint is saved: a URL whose host is
    /// a literal non-public address outside the allowlist. A host name is only
    /// judged at delivery, where the addresses it resolves to are known.
    /// </summary>
    public bool RefusesLiteralTarget(Uri url) =>
        IPAddress.TryParse(url.IdnHost.Trim('[', ']'), out var address) &&
        !AllowsHost(url.IdnHost) &&
        !AllowsAddress(address);

    public static bool IsPublic(IPAddress address)
    {
        var candidate = Normalize(address);
        if (candidate.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return false;
        }

        return !NonPublicNetworks.Any(network => network.Contains(candidate));
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool TryParseNetwork(string entry, out IPNetwork network)
    {
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            return IPNetwork.TryParse(entry, out network);
        }

        if (IPAddress.TryParse(entry, out var address))
        {
            address = Normalize(address);
            network = new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }

        network = default;
        return false;
    }

    private static string NormalizeHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();

    private static IEnumerable<string> Split(string? configured) =>
        (configured ?? string.Empty).Split(
            Separators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
