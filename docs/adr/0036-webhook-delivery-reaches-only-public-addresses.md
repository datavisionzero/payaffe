# Webhook Delivery Reaches Only Public Addresses

A Webhook Endpoint URL is typed by an Admin, and the delivery worker posts a
signed body to it from inside the operator's network. Until now it would post
to anything: loopback, the cloud metadata address, the database's private
network, and wherever a receiver's `3xx` pointed next, with the stored status
code telling the author which ports answered. The decision is that Webhook
Delivery connects only to public addresses, never follows a redirect, and
reaches a private target only when the installation names it in
`Webhooks:Delivery:AllowedPrivateTargets` (`PAYAFFE_WEBHOOK_ALLOWED_PRIVATE_TARGETS`
in Compose).

The alternative was to validate the URL when the Endpoint is saved and trust it
afterwards. It was rejected because a name is not an address: it can resolve to
a public address when it is saved and to `169.254.169.254` when it is
delivered to. The check therefore sits where the socket is opened, against the
addresses the name resolves to at that moment. Saving still refuses a literal
non-public address, so the Admin who typed one learns it at once, but that is a
courtesy and not the boundary.

The other alternative was to allow private targets by default and let an
operator switch them off. It was rejected because the installation that needs
the protection is the one whose operator did not think about it, and a shop
that receives webhooks on an internal address is one line of configuration
away from working again.

## Consequences

**A private receiver has to be named.** Host names, addresses and CIDR ranges,
comma separated. A named host is reached at whatever it resolves to; a named
address or range is reached under any name that resolves into it. Everything
listed is a target any Admin can point a delivery at, so the list names the
receiver and nothing wider. An entry that is none of the three stops the host.

**What counts as not public.** Loopback, link-local (`169.254.0.0/16`,
`fe80::/10`), private (RFC 1918, `fc00::/7`), shared address space
(`100.64.0.0/10`), unspecified, multicast and reserved ranges, and IPv4-mapped
IPv6 forms of all of them.

**A redirect is an answer, not an instruction.** A `3xx` is terminal, as the
contract always said, and nothing is sent to the `Location`.

**A refused delivery is terminal.** It records `webhook_target.not_public` in
Delivery history with no HTTP status. Retrying cannot make the target public,
and the Admin needs the reason, not a retry schedule. A name that does not
resolve at all is still a retryable DNS failure.

**No HTTP proxy.** Delivery connects directly and ignores `HTTP_PROXY`: through
a proxy the checked connection would be the proxy's and the target a request
the proxy makes on its own.

**Every host that delivers reads the list.** The worker delivers, the API and
the Admin MCP host deliver a manual resend, and the API refuses a literal
address on save, so the setting belongs to all of them.

**Upgrading can stop deliveries.** An installation whose shop receives webhooks
on an internal address sees those deliveries refused after the upgrade until
the target is allowlisted; the operations guide says how to do that first.
