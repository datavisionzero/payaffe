# A Test Installation Simulates Its External Truth

Amends [ADR 0003](./0003-blockchain-truth-comes-from-a-hosted-api-not-a-node-we-run.md)
and [ADR 0004](./0004-one-observation-provider-is-selected-and-never-mixed.md)
for installations in Test Mode only.

A developer integrating a product with `payaffe` needs to drive a Payment to
completion, underpayment, overpayment, and expiration before any real money is
involved. The hard part is not the Integration API; it is everything outside
it: a Watch-Only Wallet Source, a Hosted Blockchain API account, and a
cryptocurrency transfer for every test. So an installation can run in Test
Mode, in which Blockchain Truth, Payment Addresses, and exchange rates come
from built-in simulations, and everything else — the payment lifecycle, the
Integration API contract, webhook signing and delivery, Payment Event History,
and the Audit Log — runs unchanged.

Public testnets were considered as the sandbox instead. They keep the real
observation path, but they still need a testnet wallet, a provider that serves
the testnet, faucet funds, and minutes of block time per attempt, and they
cannot produce an underpayment or a late payment on demand. Those are exactly
the cases an integration most needs to handle, so the simulation is the
product and testnets remain something an operator can point a live-mode
installation at.

## Consequences

**The Installation Mode is fixed, not toggled.** It is `live` by default and
`test` when configured in the environment. The first host to start against an
empty database records it; every host afterwards refuses to start when its
configured mode differs from the recorded one. A database that predates the
setting is `live`. Switching modes means a new database. A runtime switch was
rejected because it creates a database holding both real and simulated
Payments, and nothing downstream could tell them apart after the fact.

**Simulated truth has one source per concern, as in live mode.** Test Mode
selects the `simulated` Blockchain Observation Mode, a simulated address source
for every Supported Currency in every Project, and fixed configurable exchange
rates. None of them exists in live mode, and live providers are not used in
Test Mode. There is still one arbiter of whether money arrived; in Test Mode it
is a Simulated Transaction someone recorded.

**Simulated addresses cannot receive mainnet funds.** BTC and LTC addresses are
testnet addresses, and native ETH instructions name a test chain ID, so a
wallet asked to pay one refuses on mainnet. Native ETH addresses have no network
prefix, so they are derived from a hash with no known key rather than taken from
the operator's pool.

**Simulation is reachable wherever a Payer can be.** The Payer Page offers the
Payer an explicit "I have paid" action. An Embedded Payment Flow has no Payer
Page, so the Integration API gains a Test-Mode-only endpoint that records a
Simulated Transaction for a Payment of the calling Project, and the SDK exposes
it. An optional amount makes Underpayment and Overpayment reachable; recording
one after Payment Expiration exercises the Late Acceptance Window.

**Test data announces itself.** Every Integration API Payment response and
every webhook payload carries a test-mode flag, and the Payer Page and Admin UI
show a persistent indicator. An integration can reject a test completion in
production by reading one field.

**The simulation surfaces are absent in live mode.** Not disabled, not guarded
by a flag: the routes, the Payer Page action, and the observation mode are not
registered, so there is nothing in a live installation to misconfigure.

**The non-custodial boundary does not move.** Test Mode holds no key, spends
nothing, and adds no refund or withdrawal path.
