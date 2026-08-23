# Hosted Blockchain API Options

This document evaluates whether `payaffe` can realistically use Hosted Blockchain APIs as Blockchain Truth for BTC, LTC, and native ETH.

The MVP decision is to support selectable hosted Blockchain Observation Modes. Monero is out of scope for the MVP.

## Initial Finding

Without Monero, Hosted Blockchain APIs become much more realistic for the MVP. BTC, LTC, and ETH are widely supported by infrastructure providers, and the expected usage of 5 to 20 payments per day is far below most paid production quotas.

Free tiers are plausible for development and very small private deployments, but they should not be treated as a reliable production baseline. A realistic production budget is roughly EUR/USD 0 to 50 per month for a single-provider setup, depending on whether the chosen provider allows production use on a free plan and whether support/SLA matters.

## Expected Usage

A conservative polling-heavy design might check 20 active Payments every minute for 24 hours:

- 20 payments * 60 minutes * 24 hours = 28,800 checks/day
- about 864,000 checks/month

Actual usage can be much lower if the system polls only active payment addresses, uses adaptive polling, and stops polling after expiration/finality. This matters because several providers offer free or cheap quotas that can cover the MVP volume if polling is disciplined.

For the MVP, Blockchair should not be treated as a block-scanning provider for all supported currencies. BTC and LTC block-oriented observation can be efficient, but ETH produces far more blocks and Blockchair's useful state-change endpoint is costed per block. The Blockchair mode should therefore use sparse polling of active payment addresses.

## Candidate Providers

### NOWNodes

Relevant coverage:

- BTC
- LTC
- ETH

Pricing signals:

- Free Start plan: 100,000 requests, 15 RPS, 5 networks, 1 month only.
- Pro plan: 1,000,000 requests/month for EUR 20/month.
- Business plan: 30,000,000 requests/month for EUR 200/month.

Assessment:

- Strong fit for a simple low-cost MVP because one paid plan covers all target currencies.
- The EUR 20/month Pro plan is the cleanest paid baseline found so far.
- The free tier is useful for validation, not production, because it lasts one month.

Sources:

- https://nownodes.io/pricing
- https://nownodes.io/nodes/bitcoin-btc
- https://nownodes.io/nodes/litecoin-ltc
- https://nownodes.io/nodes/ethereum-eth

### Chainstack

Relevant coverage:

- BTC
- LTC is listed, but only as a dedicated deployment rather than elastic.
- ETH is supported.

Pricing signals:

- Developer plan: free, 3M request units/month, 25 RPS, 1 node.
- Growth plan: USD 49/month, 20M request units/month.
- Extra usage starts at USD 20 per 1M request units on the Developer plan and decreases on higher tiers.

Assessment:

- Potentially attractive if the dedicated LTC deployment fits the operating-cost goal.
- The free Developer plan is unusually generous for expected MVP volume.
- The "1 node" limit and Litecoin's dedicated-only listing may make multi-currency production awkward or push the real cost above the free tier.

Sources:

- https://chainstack.com/pricing/
- https://docs.chainstack.com/docs/protocols-networks

### QuickNode

Relevant coverage:

- BTC
- LTC
- ETH

Pricing signals:

- Free trial: USD 0, 1 month, 10M API credits, 15 RPS, 1 endpoint.
- Build plan: USD 49/month, 80M API credits, 50 RPS, 10 endpoints.
- Additional API credits on Build: USD 0.62 per 1M credits.

Assessment:

- Solid production option once the product needs a paid plan and multiple endpoints.
- More expensive than NOWNodes for the MVP if the only requirement is basic BTC/LTC/ETH payment observation.
- The free option is a trial, not a stable production tier.

Sources:

- https://www.quicknode.com/pricing
- https://www.quicknode.com/api-credits

### Blockchair

Relevant coverage:

- BTC
- LTC
- ETH

Pricing signals:

- Free/no-key trial: up to 1,000 calls/day for a few days.
- Pay-as-you-go: USD 1 per 1,000 calls, with 1-year expiry.
- Monthly subscriptions start at USD 25/month for 1,250 calls/day.
- Free plan has a hard limit of 30 requests/minute.

Assessment:

- Viable as a primary mode only if polling is sparse and limited to active payment addresses.
- Not preferred for chain-wide block scanning across BTC, LTC, and ETH because ETH block volume makes per-block observation unattractive.
- Useful for operators who value Blockchair's longevity and independence from NOWPayments.
- Less attractive if the system polls frequently; 864,000 checks/month would cost about USD 864 on pay-as-you-go.

Sources:

- https://blockchair.com/api
- https://blockchair.com/api/plans
- https://blockchair.com/api/docs

### BlockCypher

Relevant coverage:

- BTC
- LTC
- ETH

Pricing signals:

- Free tier: 3 requests/second and 100 requests/hour for classic requests.
- Webhooks/WebSockets: 100 sent events/hour on the free tier.
- Paid plans start at USD 119/month.

Assessment:

- Functional coverage fits the reduced currency set.
- Free tier may be enough for development or very careful low-volume polling.
- Paid entry price is high compared with NOWNodes, Chainstack, and QuickNode for this MVP.

Source:

- https://www.blockcypher.com/dev/bitcoin/

### Alchemy

Relevant coverage:

- ETH is strongly supported.
- BTC and LTC are not the obvious fit for Alchemy's core product positioning and must be verified before considering it as a unified provider.

Pricing signals:

- Free plan: 30M compute units/month.
- Pay As You Go: USD 0.45 per 1M compute units for the first 300M, then USD 0.40 per 1M compute units.
- Pricing page estimates 10M monthly requests below 300 RPS at USD 104/month, depending on method mix.

Assessment:

- Good ETH candidate.
- Not a preferred single-provider candidate for BTC/LTC/native ETH unless BTC and LTC support is explicitly confirmed for the required endpoints.

Source:

- https://www.alchemy.com/pricing

### Tatum

Relevant coverage:

- BTC
- LTC
- ETH

Pricing signals:

- Free plan: 100,000 credits, 3 RPS, 2 API keys.
- Paid plans use credit and RPS packages; dedicated keys start higher than the low-cost options above.

Assessment:

- Broad platform, likely viable technically.
- Less clearly cost-optimal than NOWNodes or Chainstack for this MVP.

Sources:

- https://docs.tatum.io/docs/plans-limits
- https://tatum.io/pricing

## Cost Comparison

| Provider | BTC | LTC | ETH | Lowest Production-Looking Cost | Notes |
| --- | --- | --- | --- | --- | --- |
| NOWNodes | yes | yes | yes | EUR 20/month | Best simple paid baseline. |
| Chainstack | yes | dedicated-only | yes | USD 0/month or USD 49/month if endpoint limits fit | Free tier is attractive, but Litecoin may push the real setup out of the free path. |
| QuickNode | yes | yes | yes | USD 49/month | Strong paid option; free tier is only a trial. |
| Blockchair | yes | yes | yes | USD 25/month or pay-as-you-go | Viable only with sparse polling of active payment addresses. |
| BlockCypher | yes | yes | yes | USD 119/month | Good coverage, comparatively expensive. |
| Alchemy | not preferred | not preferred | yes | USD 0+ for ETH only | ETH candidate, not unified BTC/LTC/ETH choice. |
| Tatum | yes | yes | yes | unclear/free for low volume | Needs deeper pricing validation before selection. |

## Product Feasibility

Hosted APIs are realistic for BTC, LTC, and native ETH if the product accepts provider trust as part of Blockchain Truth.

The accepted MVP paths are:

- **Blockchair sparse polling**: poll only active payment addresses that are expecting deposits, with an admin-configurable interval.
- **NOWNodes**: use NOWNodes as the hosted node/API provider, with the operator accepting its commercial relationship with NOWPayments.

The MVP should expose these as mutually exclusive Blockchain Observation Modes. It should not automatically mix Blockchair and NOWNodes during normal payment detection.

The implementation should still provide a provider abstraction. Blockchair and NOWNodes are the first supported adapters, and later Hosted Blockchain API providers should be addable through software updates without changing the payment lifecycle model or the Integration API contract.

## Decision Pressure

Remaining decision pressure:

- whether a free tier may be used in production or only for development,
- whether provider outages pause payment acceptance or only delay settlement,
- how to disclose provider trust to admins,
- whether operators can bring their own API provider beyond providers supported by software updates,
- the exact confirmation and reorg policy per supported currency.
