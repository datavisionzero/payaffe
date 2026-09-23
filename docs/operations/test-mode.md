# Test Mode

A Test Mode installation exists so that the developer of an integrating product
can exercise the whole integration — Payment creation, Currency Selection, the
Payer Page or an Embedded Payment Flow, webhooks and polling — without a wallet,
a blockchain provider account, or any cryptocurrency
([ADR 0033](../adr/0033-a-test-installation-simulates-its-external-truth.md)).
It is not a way to test payaffe itself, and it never takes a real payment.

What is simulated, and nothing else is:

- **Payment Addresses.** Every Supported Currency is available in every Project
  without a Watch-Only Wallet Source or an ETH address pool. BTC and LTC
  addresses are testnet addresses and native ETH instructions name the Sepolia
  chain, derived from a hash so that nobody holds a key for them.
- **Exchange rates.** Fixed rates, configurable per pair; no CoinGecko request
  is made.
- **Blockchain Observation.** The `simulated` mode reports the transfers someone
  simulated. Everything after that — confirmations, Underpayment and
  Overpayment rules, the Late Acceptance Window, Payment Event History, the
  Audit Log, signed Webhook Delivery — is the same code a live installation
  runs.

## Starting one

Use the published compose file with the test-mode example environment, in a
directory of its own so that it gets its own database volume:

```sh
mkdir payaffe-test && cd payaffe-test
base=https://raw.githubusercontent.com/datavisionzero/payaffe/main/deploy
curl -O    "$base/compose.yaml"
curl -o .env "$base/test-mode.env.example"
# edit .env: a database password, and a webhook secret if you test webhooks
docker compose up -d
docker compose --profile operations run --rm migrations \
  bootstrap-admin --username dev@example.test
```

An agent setting it up follows [agent-setup.md](agent-setup.md), which creates
the first Admin without a terminal.

`PAYAFFE_INSTALLATION_MODE=test` is the whole switch. The first host to start
records the mode in the database, and from then on every host, including the
Admin MCP host, refuses to start when it is configured for the other mode. A
test installation therefore cannot be turned into a live one by editing `.env`,
and a live database cannot be opened in Test Mode: switching means a new
database. `PAYAFFE_BLOCKCHAIN_OBSERVATION_MODE` stays unset; a provider is
refused in Test Mode.

In the Admin UI, create a Project if you want more than the default one, an
Integration API Credential for your integration, and a Webhook Endpoint that
points at your integration's receiver. The Admin UI and every Payer Page carry a
"Test mode" banner, and there is nothing to configure under Addresses.

Webhook Delivery reaches only public addresses
([ADR 0036](../adr/0036-webhook-delivery-reaches-only-public-addresses.md)). A
receiver on your own machine or network — `host.docker.internal`, another
container, a LAN address — is refused until `PAYAFFE_WEBHOOK_ALLOWED_PRIVATE_TARGETS`
names it.

## Paying

A Payment is paid by simulating the transfer once its currency has been
selected:

- **Payer Page.** The page offers "I have paid", plus an underpayment of half the
  amount and an overpayment of half again.
- **Embedded Payment Flow.** The integration's backend calls
  `SimulatePaymentAsync` in the SDK, or
  `POST /api/v1/payments/{paymentId}/simulated-transactions` with an optional
  `amount`. The route exists only in a Test Mode installation.

The simulated transfer is reported on the next observation poll without
confirmations, which makes the Payment `observed`, and fully confirmed on the
poll after that, which completes it when the amount is enough. With the example
intervals that takes about ten seconds, and each step is delivered as a webhook
and visible to polling exactly as for a real transfer. A smaller amount stays
`observed` as an Underpayment until a further simulated transfer tops it up. To
see expiration, give the Project a short Payment Expiration and leave a Payment
unpaid; a transfer simulated after it expires exercises the Late Acceptance
Window.

## Keeping test and live apart

Every Payment response carries `testMode` and every webhook `test_mode`. A test
installation can be given a production integration's webhook URL and a valid
secret, and then its deliveries verify, so the signature is not what proves the
money was real: a production integration must refuse to fulfil anything marked
as test. The embedded checkout sample shows one way, with a per-storefront
`AcceptTestPayments` switch that is off unless a developer turns it on.
