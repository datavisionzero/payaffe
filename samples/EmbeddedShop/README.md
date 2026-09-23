# Embedded checkout sample

A small shop that accepts cryptocurrency through Payaffe without ever sending its
customer to Payaffe. The payer stays on the shop's pages, the shop renders its own
checkout, and the only thing that talks to Payaffe is this shop's backend.

It exists to be read and copied. Nothing here is deployed by the payaffe project,
and the shop deliberately keeps its orders in memory, because a real order book is
the one part every product already has.

## What it demonstrates

- creating a Payment with the order identifier as both External Reference and
  `Idempotency-Key`, so a retried creation returns the same Payment, and an
  order whose creation timed out is resumed with that key rather than placed
  again;
- showing the Payment Options the Project can actually serve, with the stable
  reason code for an option it cannot;
- Currency Selection, including the losing half of a race and a missing exchange
  rate;
- the Payment Instruction: exact amount, address, wallet URI, and a QR code
  rendered by this backend and served from this origin, in explicit colours
  because an `<img>` inherits none from the page;
- expiry that does not close the order, because a late transfer can still settle;
- Webhook Delivery that is verified against the raw bytes, deduplicated by event
  identifier, and only then allowed to fulfil the order — and only when it
  describes the Payment the shop created for that order, for its amount and
  currency, because the External Reference finds an order but authorizes
  nothing;
- polling as the second channel, one loop per order however often a currency is
  chosen, for the Delivery that never arrives, which keeps going through
  failures and stops only on a terminal status or a refusal;
- two storefronts, each with its own Payaffe Project, credential, webhook secret
  and orders, which cannot see each other.

## Running it

The sample needs two Payaffe Projects and one Integration API credential per
storefront. Nothing is shipped in the repository: configure the four secrets and
the base addresses through user secrets, environment variables, or your own secret
store.

```bash
cd samples/EmbeddedShop
dotnet user-secrets set "Shop:Storefronts:teahouse:PayaffeBaseAddress" "https://payaffe.example.com"
dotnet user-secrets set "Shop:Storefronts:teahouse:PayaffeApiToken" "<integration api token>"
dotnet user-secrets set "Shop:Storefronts:teahouse:WebhookSecret" "<webhook endpoint secret>"
dotnet user-secrets set "Shop:Storefronts:roastery:PayaffeBaseAddress" "https://payaffe.example.com"
dotnet user-secrets set "Shop:Storefronts:roastery:PayaffeApiToken" "<other project's token>"
dotnet user-secrets set "Shop:Storefronts:roastery:WebhookSecret" "<other project's secret>"
dotnet run
```

The shop starts at `/teahouse/` and `/roastery/`. Point each Project's Webhook
endpoint at `https://<this shop>/<storefront>/webhooks/payaffe`. A storefront with
a missing token or secret fails at startup rather than at the first checkout.

Run the tests, which need no Payaffe installation, with:

```bash
dotnet test samples/EmbeddedShop.Tests/EmbeddedShop.Tests.csproj
```

### Against a Test Mode installation

The quickest way to see the whole flow is a payaffe installation in Test Mode
([docs/operations/test-mode.md](../../docs/operations/test-mode.md)), which needs
no wallet and no provider. Point a storefront at it and let that storefront
accept simulated payments:

```bash
dotnet user-secrets set "Shop:Storefronts:teahouse:AcceptTestPayments" "true"
```

The checkout then says it is in test mode and offers "Simulate the payment" once
an instruction is shown; the shop calls `SimulatePaymentAsync`, and the order is
fulfilled by the same verified webhook or reconciling read as a real one. A
storefront without that setting shows a simulated payment for what it is and
never hands goods over for it, which is what keeps a test installation pointed
at a production shop harmless. `EmbeddedShopTestModeTests` in
`tests/Payaffe.Api.Tests` runs this sample against a real Test Mode installation
and pays, underpays, overpays and expires orders through it.

## How a frontend consumes this

The page in `wwwroot/assets/checkout.js` is one frontend, not the frontend. It uses
these endpoints of this shop and no Payaffe endpoint at all:

| Endpoint | Purpose |
| --- | --- |
| `POST /{storefront}/api/orders` | Place an order and create its Payment. |
| `POST /{storefront}/api/orders/{orderId}/payment` | Retry the Payment creation of an order whose placement answered `retryable`. |
| `GET /{storefront}/api/orders/{orderId}` | The order as this shop sees it, for progress. |
| `POST /{storefront}/api/orders/{orderId}/currency` | Choose how to pay; returns the instruction. |
| `GET /{storefront}/api/orders/{orderId}/payment-code.svg` | The QR code, served from this origin. |

Every response is the shop's own shape, `OrderView` in `Contracts.cs`. It carries
no bearer token, no Payer Page URL, and no Payaffe origin. A React storefront, a
mobile app, or a server-rendered page replaces `checkout.js` and changes nothing
else: the contract a frontend depends on is this shop's, and the Payaffe contract
stops at the backend.

The one link that leaves the page is the wallet URI, which a payer taps to open
their own wallet. It is a wallet scheme, not a web origin, and following it is the
payer's action.

## What a product must add

- a real order book, with the handled Webhook Event identifiers stored in the same
  transaction that fulfils the order;
- real customer authentication, in place of the cookie this sample issues;
- the credential in a secret store, and a plan for rotating it;
- whatever "fulfil" means in that product — this one only counts it.

The rules the sample follows, and the reasons behind them, are in
[the embedded payment baseline](../../docs/architecture/embedded-payment-sdk-baseline.md)
and the [SDK README](../../src/Payaffe.Sdk/README.md).
