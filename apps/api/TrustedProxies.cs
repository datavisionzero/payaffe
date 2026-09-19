using System.Net;
// Aliased rather than imported: that namespace carries an obsolete IPNetwork
// of its own, which would shadow the one this parses into.
using ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders;

/// <summary>
/// The hops in front of this host that are allowed to name the caller. Behind a
/// reverse proxy every request arrives from the proxy, so the connection
/// address is the proxy's for every caller; `X-Forwarded-For` carries the real
/// one and is a header anybody can write. Naming the proxies is what makes the
/// difference between the two (ADR 0032).
/// </summary>
public sealed class TrustedProxies
{
    public const string ConfigurationKey = "Network:TrustedProxies";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    private TrustedProxies(IReadOnlyList<IPAddress> addresses, IReadOnlyList<IPNetwork> networks)
    {
        Addresses = addresses;
        Networks = networks;
    }

    public IReadOnlyList<IPAddress> Addresses { get; }

    public IReadOnlyList<IPNetwork> Networks { get; }

    public int Count => Addresses.Count + Networks.Count;

    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Reads the configured list of addresses and CIDR networks. An entry that
    /// is neither stops the host: a proxy list that was meant to be there and
    /// silently is not would leave every limit counting one address for the
    /// whole internet.
    /// </summary>
    public static TrustedProxies Parse(string? configured)
    {
        var addresses = new List<IPAddress>();
        var networks = new List<IPNetwork>();

        var entries = (configured ?? string.Empty).Split(
            Separators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            if (entry.Contains('/', StringComparison.Ordinal))
            {
                networks.Add(ParseNetwork(entry));
                continue;
            }

            if (!IPAddress.TryParse(entry, out var address))
            {
                throw new InvalidOperationException(Rejected(entry, "it is not an IP address or a CIDR network"));
            }

            addresses.Add(address);
        }

        return new TrustedProxies(addresses, networks);
    }

    /// <summary>
    /// The chain is walked to its end rather than cut at a fixed number of
    /// hops: the middleware stops at the first address that is not one of the
    /// named proxies, and that address is the caller. The framework defaults
    /// trust loopback; they are cleared, because what is trusted here is what
    /// the installation wrote down and nothing else.
    /// </summary>
    public void ApplyTo(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = null;

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var address in Addresses)
        {
            options.KnownProxies.Add(address);
        }

        foreach (var network in Networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    /// <summary>
    /// An entry that carries host bits beyond its prefix, such as
    /// <c>172.30.0.5/24</c>, is read as the network it names rather than
    /// refused: what was written down is a range either way.
    /// </summary>
    private static IPNetwork ParseNetwork(string entry)
    {
        try
        {
            return IPNetwork.Parse(entry);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(Rejected(entry, "it is not a CIDR network"));
        }
    }

    private static string Rejected(string entry, string reason) =>
        $"{ConfigurationKey} entry '{entry}' was rejected: {reason}.";
}
