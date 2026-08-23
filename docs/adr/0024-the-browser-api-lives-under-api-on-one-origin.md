# The Browser API Lives Under /api, on One Origin

Everything the API answers is under `/api` or `/health`. The Admin API is at
`/api/admin/...`, the Payer API at `/api/payer/...`, and the Integration API
stays where it already was, at `/api/v1/...`
([ADR 0011](./0011-the-integration-api-versions-in-the-path.md)). A reverse
proxy can therefore serve the whole installation from one address with two
rules, and the payer page, the Admin UI, and the API share an origin.

The alternative was the layout this replaced: the Admin API at `/admin/...` and
the Payer API at `/payer/...`, with the browser calling them cross-origin. It
was rejected because it could not be proxied. The Admin UI is a page at
`/admin` and the admin API was at `/admin/`, and no proxy rule splits those
apart — the two prefixes collided by construction. Anything on one origin was
ruled out before it could be considered.

What forced the question was the published web image. Next.js compiles
`NEXT_PUBLIC_*` into the browser bundle, so an image built with an API address
in it is pinned to the installation it was built for. Under the old layout
there was no address that would have been right for more than one, which meant
either publishing an image that worked at `localhost` and nowhere else, or
telling every operator to build their own. Neither is a product. On one origin
the bundle needs no address at all: it calls `/api` on whatever origin served
the page, and one image works everywhere.

The prefix is not versioned, and only `/api/v1` is. The Integration API is
consumed by systems the operator does not control and is a published contract;
the Admin and Payer APIs are consumed by the web app shipped in the same
release, and versioning a surface that only ever talks to its own build would
be ceremony.

## Consequences

**An installation needs a reverse proxy.** It needed one for TLS anyway, and
the deployment Compose file binds both published ports to the loopback
interface on that assumption. What it now also does is route: `/api/` and
`/health/` to the API host, everything else to the web host. Both are in
[docker-compose.md](../operations/docker-compose.md), with a complete
configuration.

**Cross-origin still works, and is no longer the default.** An installation
whose API answers somewhere else sets `NEXT_PUBLIC_PAYAFFE_API_BASE_URL` at
build time and names its own image. That path is supported and it is the one
that costs an image build.

**The Admin and Payer paths are not a contract.** They moved once and may move
again. Only `/api/v1` carries the promise, and the snapshot in
`docs/contracts/integration-api/` is what holds it — the move did not touch
that file, which is the check that it was really an internal change.

**The web app has no compiled-in address to get wrong.** The failure mode it
removes is a real one: an image built for one installation, deployed to
another, silently calling an API that is not there.
