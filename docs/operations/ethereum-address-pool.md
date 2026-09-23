# Native ETH Address Pool

The MVP uses an imported Address Pool for native ETH Payment Addresses.

## Rules

- `payaffe` only imports public Ethereum addresses.
- `payaffe` must not import private keys, seed phrases, keystores, or recovery phrases.
- Imported addresses should come from a wallet controlled by the operator.
- Each imported address may be assigned to at most one Payment.
- Native ETH Payment Addresses are not automatically reused after assignment.
- When the pool is low, the Admin should import more addresses before native ETH becomes unavailable.

## Import Format

The Admin backend accepts a validated list of addresses. The Admin UI may
parse a CSV with one Ethereum address per row and submit only the address
values.

Example:

```csv
address
0x0000000000000000000000000000000000000000
0x1111111111111111111111111111111111111111
```

The import should reject invalid addresses and duplicate addresses.

The protected backend operation is
`POST /api/admin/native-eth-address-pool/import` with this shape:

```json
{
  "addresses": [
    "0x0000000000000000000000000000000000000000",
    "0x1111111111111111111111111111111111111111"
  ]
}
```

It requires an authenticated Admin session, CSRF evidence, and recent Step-up
authentication. Imports are atomic and audited. Capacity is available through
`GET /api/admin/native-eth-address-pool`; the low-capacity threshold is a Project
setting. `PAYAFFE_NATIVE_ETH_LOW_CAPACITY_THRESHOLD` seeds it for the default
Project on the first start only; after that, change it on the Project, and a
different configured value is logged as a warning and ignored.

`PAYAFFE_NATIVE_ETH_NETWORK` and `PAYAFFE_NATIVE_ETH_CHAIN_ID` identify the
chain for imported addresses and for generated wallet URIs. They default to
Ethereum mainnet and chain ID `1`; configure both consistently before assigning
an imported address on another supported network.

## Recommended Provisioning Workflow

Use a dedicated Ethereum HD wallet seed for the `payaffe` receiving address pool. Generate and export the public addresses outside `payaffe`, then import only the addresses into `payaffe`.

Recommended initial workflow for technically comfortable operators:

1. Prepare an offline machine or live environment that will not be used for normal browsing.
2. Generate or access a dedicated Ethereum HD wallet seed offline.
3. Export the first batch of addresses, for example 1,000 addresses, using a deterministic derivation path.
4. Save only the public addresses to CSV.
5. Import the CSV into `payaffe`.
6. Store and back up the wallet seed outside `payaffe`.
7. Verify that the external wallet can still access the first and last imported address.

The initial documented tool candidate is the standalone offline version of `iancoleman/bip39`, because it can derive Ethereum addresses from a BIP39 mnemonic without sending data to a server when run offline. This tool can also display private keys, so it must be treated as sensitive wallet software and must not be used on an online daily-use machine.

MyEtherWallet can be used as a familiar Ethereum wallet interface for checking HD wallet derivation paths and accessing addresses, but the MVP should not depend on a specific wallet UI for bulk export.

## Product Requirements

The Admin UI should make this workflow practical:

- show unused, assigned, observed, and retired address counts,
- show a low-pool warning,
- support bulk CSV import,
- show import validation errors before saving,
- prevent duplicate imports,
- record when and by whom addresses were imported.
