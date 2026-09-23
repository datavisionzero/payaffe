# Crypto Payment Acceptance

This context describes the language for accepting cryptocurrency payments for external systems.

## Language

**Payment**:
A business record representing an expected cryptocurrency payment and its lifecycle from creation to completion or manual resolution.
_Avoid_: Transaction, invoice

**Payment Status**:
The current lifecycle label of a Payment as exposed to integrations, Payers, and Admins.
_Avoid_: Internal state, database state

**Pending Currency Selection**:
A Payment that has been created but for which the Payer has not yet selected a Supported Currency.
_Avoid_: Waiting for payment, unpaid

**Waiting For Payment**:
A Payment that has a selected Supported Currency, Rate Lock, expected cryptocurrency amount, and Payment Address, but no Matching Blockchain Transaction has been observed yet.
_Avoid_: Pending transaction, unpaid

**Payer**:
The person who sends cryptocurrency to complete a Payment.
_Avoid_: Customer, user

**Payer Page**:
The `payaffe` web page where the Payer selects a Supported Currency, receives payment instructions, and sees Payment status.
_Avoid_: Checkout, invoice page

**Embedded Payment Flow**:
A product-owned Payer interface that uses the authenticated Integration API
through that product's backend instead of sending the Payer to the Payer Page.
_Avoid_: Embedded Payer Page, direct browser integration

**Single-Operator Installation**:
A deployment administered by exactly one operator or shop and containing one or more Projects.
_Avoid_: Tenant, merchant platform, marketplace

**Installation Mode**:
Whether an installation handles real payments (`live`) or simulated ones (`test`); fixed when the installation's database is first created.
_Avoid_: Environment, sandbox flag

**Test Mode**:
The Installation Mode in which Blockchain Truth, Payment Addresses, and exchange rates are simulated so an integration can be exercised without real funds.
_Avoid_: Sandbox, demo mode, dry run

**Simulated Transaction**:
A Blockchain Transaction recorded on request in Test Mode that the simulated Blockchain Observation Mode reports as if it had been observed on-chain.
_Avoid_: Fake payment, test payment

**Project**:
An operator-defined boundary that owns payment data, Integration API Credentials, receiving-address allocation, Webhook Delivery, and payment-policy configuration inside one Single-Operator Installation.
_Avoid_: Tenant, shop, workspace

**Selected Project**:
The Project an Admin is currently viewing or acting in; it prevents accidental mixing but does not restrict the Admin's installation-wide permission.
_Avoid_: Active tenant, membership, role

**Project Status**:
The lifecycle label controlling whether a Project accepts new Payments, finishes existing work, or is retained as read-only history.
_Avoid_: Tenant status, deletion state

**Blockchain Transaction**:
An on-chain transfer observed on a supported blockchain.
_Avoid_: Payment

**Matching Blockchain Transaction**:
A Blockchain Transaction for the Payment's selected Supported Currency and payment address that is eligible to count toward Payment completion.
_Avoid_: Deposit, installment

**Blockchain Observation**:
The system's process for detecting and verifying relevant on-chain activity for Payments.
_Avoid_: Provider callback, explorer lookup

**Observed Payment**:
A Payment for which at least one Matching Blockchain Transaction has been detected but the Payment has not yet reached completion.
_Avoid_: Paid payment, pending transaction

**Blockchain Truth**:
The source the system accepts as authoritative for deciding whether a Payment has been observed on-chain.
_Avoid_: Blockchain data, API response

**Observed Payment Time**:
The timestamp used by `payaffe` to decide whether a Blockchain Transaction falls inside Payment Expiration or the Late Acceptance Window.
_Avoid_: Send time, customer payment time

**Blockchain Observation Mode**:
The admin-selected strategy that supplies Blockchain Truth for payment detection in one installation.
_Avoid_: Hybrid provider setup, fallback routing

**Observation Health**:
The current availability of Blockchain Observation for a Supported Currency.
_Avoid_: Provider status, API health

**Hosted Blockchain API**:
A third-party hosted API that exposes blockchain data or node RPC access without the operator running the underlying infrastructure.
_Avoid_: External API, provider

**Fiat Amount**:
The expected payment value denominated in EUR or USD before it is converted into a cryptocurrency amount.
_Avoid_: Price, crypto amount

**Exchange Rate Source**:
The configured source `payaffe` uses to convert Fiat Amounts into cryptocurrency amounts.
_Avoid_: Price API, market data provider

**Rate Lock**:
The exchange rate captured when the Payer selects a Supported Currency.
_Avoid_: Quote, price lock

**Rate Cache**:
The stored exchange-rate data used to reduce calls to the Exchange Rate Source.
_Avoid_: Price cache, quote cache

**Stale Rate**:
A cached exchange rate older than the normal Rate Cache interval but still within the configured maximum stale age.
_Avoid_: Old price, fallback price

**Payment Option**:
A selectable Supported Currency presented to the Payer before the Payment is fixed to one cryptocurrency amount and address.
_Avoid_: Alternative payment, coin choice

**Payment Address**:
The cryptocurrency address assigned to a Payment after the Payer selects a Supported Currency.
_Avoid_: Wallet, account

**Payment Instruction**:
The immutable Supported Currency, network, exact amount, Payment Address, and
wallet URI returned after Currency Selection.
_Avoid_: QR code, invoice, transaction

**Address Pool**:
A managed set of pre-provisioned Payment Addresses that `payaffe` may assign to Payments.
_Avoid_: Wallet, account list

**Payment Expiration**:
The configured time limit after which an unpaid Payment leaves the regular payment window.
_Avoid_: Timeout, cancellation

**Late Acceptance Window**:
The configured time after Payment Expiration during which a matching late Blockchain Transaction may still complete a Payment automatically.
_Avoid_: Grace period, timeout extension

**Supported Currency**:
A cryptocurrency that the system can accept for payments.
_Avoid_: Coin

**Native ETH**:
Ether transferred as the native Ethereum asset, not as an ERC-20 token.
_Avoid_: ETH token

**Watch-Only Wallet Source**:
Project-bound wallet data that lets `payaffe` derive or know receiving addresses without being able to spend funds.
_Avoid_: Wallet, private key, seed

**Key Custody**:
The ability to hold secrets that can spend received cryptocurrency.
_Avoid_: Wallet management

**Underpayment**:
A payment where the observed received amount is less than the expected amount.
_Avoid_: Partial payment

**Overpayment**:
A payment where the observed received amount is greater than the expected amount.
_Avoid_: Surplus, refund case

**Payment Tolerance**:
The configured percentage by which an underpaid Payment may still be accepted automatically.
_Avoid_: Discount, fee

**Confirmation Requirement**:
The configured number of blockchain confirmations required before an Observed Payment is completed.
_Avoid_: Finality, safety level

**Reorg Alert**:
An Admin-visible warning that a completed Payment's Matching Blockchain Transaction was affected by a blockchain reorganization.
_Avoid_: Payment reversal, uncomplete

**Reorg Monitoring Depth**:
The configured number of blocks after completion during which `payaffe` monitors for blockchain reorganizations affecting completed Payments.
_Avoid_: Finality depth, rollback window

**Settlement**:
An administrative decision that marks a payment as resolved when automated rules alone should not decide the outcome.
_Avoid_: Close, finish

**Admin**:
A person with installation-wide permission to operate every Project and resolve payment workflows.
_Avoid_: User, operator

**Admin Account**:
A local account used by an Admin to access the Admin UI or authorized admin surfaces.
_Avoid_: User account, operator account

**Audit Log**:
An append-only record of security-relevant or payment-relevant administrative actions.
_Avoid_: Activity feed, history

**Payment Event History**:
The record of lifecycle changes for a Payment.
_Avoid_: Audit log, webhook log

**Integration API**:
The API surface used by external systems to create, inspect, or react to payments.
_Avoid_: Backend API, public API

**External Reference**:
The external system's required identifier for relating a Payment to its own business record inside a Project.
_Avoid_: Order ID, metadata

**Idempotency Key**:
A required request key that lets `payaffe` return the same Payment when an external system retries Payment creation.
_Avoid_: Request ID, retry token

**Integration API Credential**:
The Project-owned credential used by an external system to authenticate with the Integration API and select exactly one Project.
_Avoid_: API user, token

**Webhook Delivery**:
An attempt by `payaffe` to notify an external system about a Payment event.
_Avoid_: Callback, push message

**Webhook Endpoint**:
An external URL configured for an Integration API Credential to receive selected Payment events.
_Avoid_: Callback URL, listener

**Payment Context Field**:
An optional structured field supplied by an external system to help Admins identify the business context of a Payment.
_Avoid_: Metadata, custom JSON

**Return URL**:
An optional external-system URL shown to the Payer after Payment completion.
_Avoid_: Redirect URL, callback URL
