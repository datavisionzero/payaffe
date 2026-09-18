# Secrets Live in the Environment, Never in the Repository

The product handles database credentials, admin password and TOTP material,
session and CSRF secrets, integration bearer tokens, webhook endpoint secrets,
blockchain provider keys, exchange rate keys, and observability credentials —
all while being non-custodial. Configuration for every one of them is
server-side, supplied as environment variables or a non-versioned `.env`.

A dedicated secret store was rejected as a requirement, not as an option. Vault
is the right answer for an organisation and the wrong one to mandate for a shop
running a compose file; SOPS, age, Docker secrets, systemd environment files,
and a real secret store all remain available to an installation that documents
its choice. What is mandated is the negative: no secret in Git, no secret in a
container image, no secret in a log, and no secret in a browser bundle.

`.env.example` is versioned and therefore contains only placeholders or
local-only non-production defaults. Browser build values such as Vite `VITE_*`
variables are treated as published text, because they are.

## Consequences

**Start-critical configuration is validated at startup and fails fast.** An
invalid setting should stop the host with a message naming the key, not surface
three hours later as a confusing runtime error. Optional integrations may stay
disabled when their secrets are absent, provided the disabled state is visible
rather than silent.

**Plaintext is a one-time event.** Integration bearer tokens and recovery codes
are shown once at creation and stored only as hashes. Webhook secrets and
provider keys are stored as secrets and never copied into logs, audit entries,
delivery history, OpenAPI examples, MCP snapshots, or webhook contract
snapshots — a list worth stating explicitly, because each of those is a place a
secret has plausibly ended up in some product.

**Leakage is something to be tested, not assumed.** A deliberate suite asserting
that no surface echoes a token, a webhook secret, an extended public key, a TOTP
secret, or a provider key is what makes the paragraph above true next year.

**Rotation is documented per secret class before production use.** Manual
rotation is acceptable; undocumented rotation is not, because the thing that
goes wrong is the overlap window and the clients that had to be told.

The detailed rules are in
[secrets-configuration-baseline.md](../architecture/secrets-configuration-baseline.md).
