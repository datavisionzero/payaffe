# The Client Address Is the Connection, Until a Proxy Is Named

Every rate limit this host partitions by source address, and every Audit Log
entry that records one, uses the address the connection came from. Behind the
reverse proxy an installation is told to put in front of it
([ADR 0024](./0024-the-browser-api-lives-under-api-on-one-origin.md)), that
address is the proxy's for every caller in the world. `X-Forwarded-For` carries
the real one, and it is a header anybody can write. The decision is that the
host reads it only from hops the installation has named, in
`Network:TrustedProxies`, and otherwise does not read it at all.

The alternative was to trust the header whenever it is present, which is what
most deployments do by accident. It was rejected because it is worse than the
problem: an unnamed proxy costs every caller one shared budget, while a trusted
header lets any caller pick which budget to spend and which address the Audit
Log attributes an admin login attempt to. A limit that can be evaded by writing
a header is not a limit, and an audited address that the audited party chose is
not evidence.

The other alternative was to trust the header only when the connection comes
from a private network, on the assumption that nothing else can reach the port.
It was rejected because that assumption is the operator's to make, not ours:
the deployment Compose file binds to the loopback interface but does not have
to, and a container network is not a boundary the product can see.

## Consequences

**An installation behind a proxy has to say so.** One line in `.env`, and the
value is the address or CIDR range the proxy connects from — for the Compose
baseline, the bridge network the reverse proxy shares with `api`. Until it is
set, the admin login limit, the browser error limit, and the source address in
the Audit Log are the proxy's, which is to say one bucket and one address for
everybody. The host says which of the two it is doing in one line at startup.

**Every hop is named, or the chain stops there.** The forwarded chain is walked
from the connection outwards and ends at the first address that was not named,
so an installation with a CDN in front of its own proxy lists both. Listing
neither is safe and listing too much is not: a range wide enough to contain
real callers lets those callers name themselves.

**A wrong list stops the host.** An entry that is not an address or a CIDR
network fails the start with the entry quoted. A proxy list that was meant to
be there and silently was not would leave the installation with exactly the
behaviour this ADR exists to remove, and it would look like it was configured.

**The scheme comes from the same source.** `X-Forwarded-Proto` is taken from
the same trusted hops, so a host that knows it is reached over TLS is one that
was told by something it trusts.
