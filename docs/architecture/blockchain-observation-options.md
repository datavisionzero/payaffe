# Blockchain Observation Options

This document tracks operator-controlled alternatives to Hosted Blockchain APIs.

These options are not the accepted MVP default. They remain relevant if a future Blockchain Observation Mode lets operators avoid Hosted Blockchain APIs.

## BTC

Candidate approaches:

- BIP 157/158 compact block filters with local matching.
- Pruned Bitcoin Core plus local wallet or local indexing.
- A local purpose-built indexer backed by an operator-controlled node.

Important trade-offs:

- Compact filters reduce storage and improve privacy versus server-side address filtering, but still require careful peer and reorg handling.
- A pruned node validates the chain while reducing disk usage, but pruned mode has operational limits such as incompatibility with some rescans and indexing modes.
- Local indexing gives the application a clean query model, but introduces another component to operate and back up.

## LTC

Candidate approaches:

- Mirror the BTC-family approach where Litecoin Core support allows it.
- Use a pruned Litecoin Core node plus local payment tracking.
- Use a local purpose-built indexer backed by an operator-controlled Litecoin node.

Important trade-offs:

- Litecoin may not match Bitcoin Core behavior exactly in every relevant feature.
- We need to verify compact-filter, pruning, wallet, and indexing support against the Litecoin Core version selected for the MVP.

## Native ETH

Candidate approaches:

- Run an operator-controlled Ethereum node using execution and consensus clients with a non-archive sync mode.
- Use an Ethereum light client only if a production-ready option exists for the required payment-detection semantics.
- Use a local indexer against an operator-controlled Ethereum node.

Important trade-offs:

- Ethereum light clients are still a moving area; ethereum.org currently notes that known light-client implementations are not considered production-ready.
- A snap-synced Ethereum node avoids archive-node storage, but still has much higher disk requirements than BTC/LTC compact-filter style approaches.
- A local indexer can make payment queries reliable for the application, but does not remove the underlying node requirement.

## Decision Pressure

A future operator-controlled Blockchain Observation Mode would need to specify:

- required components per currency,
- storage expectations,
- sync time expectations,
- reorg handling,
- confirmation policy,
- failure behavior when observation is unavailable,
- whether `payaffe` holds keys or only watches addresses.

## Sources

- Bitcoin BIP 157: https://github.com/bitcoin/bips/blob/master/bip-0157.mediawiki
- Bitcoin BIP 158: https://github.com/bitcoin/bips/blob/master/bip-0158.mediawiki
- Bitcoin Core pruning: https://bitcoin.org/en/full-node
- Ethereum light clients: https://ethereum.org/developers/docs/nodes-and-clients/light-clients/
- Ethereum Portal Network: https://ethereum.org/developers/docs/networking-layer/portal-network/
- Geth snap sync: https://geth.ethereum.org/docs/fundamentals/sync-modes
