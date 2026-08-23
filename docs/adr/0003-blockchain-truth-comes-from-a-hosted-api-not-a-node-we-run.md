# Blockchain Truth Comes From a Hosted API, Not a Node We Run

Whether a payment has been paid is decided by a third-party blockchain API. The
honest way to state this is that the provider can, in principle, tell an
installation that money arrived when it did not, and the installation will
believe it.

The alternative is running full nodes for BTC, LTC, and native ETH. It is the
correct answer for trustlessness and the wrong one for this product: three
chains of archival storage, bandwidth, initial sync, monitoring, and upgrade
duty, imposed on an operator taking fifteen payments a day at ten to twenty
euros each. The trust that buys is real, and it costs more than the payments it
protects are worth. Pruned nodes, BIP 157/158 filters, and light clients were
all considered as a middle path; each still needs a node the operator runs, and
the operational burden — not the disk — is what makes them unattractive here.

So the trade is made explicitly rather than quietly: `payaffe` accepts a hosted
API as Blockchain Truth, and says so in its documentation instead of implying a
verification it does not perform.

## Consequences

**Confirmation requirements are the mitigation that remains.** They are the only
lever left against a provider that is wrong or a chain that reorganises, which
is why they are configurable per currency and default conservatively — one block
for BTC and LTC, twelve for native ETH.

**Reorgs are surfaced, not reversed.** A completed payment stays completed for
the external system, because an integration that has already shipped goods
cannot act on a retraction. What a reorg produces is a Reorg Alert for the
admin, monitored to a configurable depth. That asymmetry is deliberate: the
product would rather be wrong visibly than inconsistently.

**Provider outage is a first-class state, not an error.** Observation Health is
tracked per currency, an unavailable currency stops being offered as a payment
option, and payments already in flight stay valid and are reconciled when
observation recovers.

**An operator who wants their own node is not served by the MVP.** That remains
a possible future observation mode rather than a promise, and it is the
decision to reopen if the trust assumption stops being acceptable.

Which providers are available and how they are selected is
[ADR 0004](./0004-one-observation-provider-is-selected-and-never-mixed.md). The
provider comparison behind both is
[hosted-blockchain-api-options.md](../architecture/hosted-blockchain-api-options.md).
