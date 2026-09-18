# Product Requirements

## Goals

- Accept cryptocurrency payments for external systems.
- Provide a web-based frontend with a matching backend.
- Provide an Integration API so other systems can connect to `payaffe`.
- Provide a .NET 10 SDK so other products can render the complete payment flow
  without depending on the hosted Payer Page.
- Provide an admin MCP surface for querying system data and performing selected payment workflow actions.
- Keep the product self-hostable and economical to operate.

## Instance Model

One deployment serves exactly one operator or shop. The operator may create
multiple Projects to isolate its products or integrations without creating
separate installations.

Each Project owns its Payments, Integration API Credentials, receiving-address
allocation, Webhook Delivery, and payment-policy configuration. All Admin
Accounts are installation-wide and may administer every Project. The MVP has no
Project memberships, Project-specific Admins, or Project roles.

Projects are active, disabled, or archived. Disabling rejects new Payment
Creation while existing Payments, Payer Pages, Blockchain Observation,
Webhook Delivery, polling, and administrative resolution continue. Archiving
is allowed only after project work is terminal; it retains read-only history
and never deletes payment or audit evidence. Detailed ownership and lifecycle
rules follow the
[Project isolation baseline](../architecture/project-isolation-baseline.md).

## Payment Creation

External systems create Payments from fiat amounts denominated in EUR or USD.

Payment Creation requires:

- Fiat Amount,
- External Reference,
- Idempotency Key.

When a Payment is created, the Integration API returns technical payment data
for the external system, including at least the Payment identifier, current
status, Payer Page URL, configured expiration time, and Payment Options.

If the external system retries Payment Creation with the same Idempotency Key, `payaffe` returns the same Payment instead of creating a duplicate Payment.

Idempotency is scoped to the Integration API Credential within its Project.

- The same Idempotency Key with the same Payment Creation data returns the same Payment.
- The same Idempotency Key with different Payment Creation data returns a conflict error.
- Idempotency records are retained at least as long as the corresponding Payment is stored.

Payment Creation may include optional Payment Context Fields:

- username, which may contain an email address, account ID, or other external user label,
- customer number,
- cart name or product name,
- note.

Each Payment Context Field is optional and has a maximum length of 255 characters.

The MVP does not support arbitrary custom metadata JSON for Payment Creation.

Payment Context Fields are intended for Admin UI identification, filtering, and search. They are not shown on the Payer Page by default.

The integrating product chooses one of two presentation modes:

- send the Payer to the hosted Payer Page; or
- render an Embedded Payment Flow and call the authenticated Integration API
  from its own backend.

Both modes present the available Payment Options and invoke the same Currency
Selection operation. The integrating product owns its customer authentication
and order authorization; its Integration API Credential is never exposed to
the Payer's client.

After the Payer selects a Supported Currency, the Payment is fixed to exactly
one Rate Lock and Payment Instruction containing the expected cryptocurrency
amount, its atomic-unit value, network, Payment Address, and wallet URI.

The MVP does not support completing one Payment through multiple Supported Currencies at the same time. If BTC, LTC, and native ETH are available, they are alternatives before Payer selection, not simultaneous expected payments after selection.

## Exchange Rates

The MVP uses CoinGecko as the initial Exchange Rate Source.

The Exchange Rate Source may require an Admin-configured API key when needed.

The Payer's cryptocurrency amount is calculated and fixed when the Payer selects a Supported Currency.

That captured exchange rate is the Rate Lock for the Payment.

Payment Creation does not lock an exchange rate. Payment Creation records the Fiat Amount and available Payment Options.

Rate Locks remain valid until Payment Expiration.

`payaffe` uses a Rate Cache to reduce calls to the Exchange Rate Source.

Admins can configure the installation-wide Rate Cache interval.

Admins can configure the installation-wide maximum Stale Rate age.

The default maximum Stale Rate age is 30 minutes.

If the Exchange Rate Source is unavailable, `payaffe` may use a Stale Rate when it is still within the configured maximum Stale Rate age.

If no usable exchange rate is available for a Supported Currency, that Payment Option is disabled on the Payer Page.

Unavailable exchange rates for one Supported Currency do not make the whole Payer Page unusable when other Payment Options still have usable rates.

The MVP does not support automatic multi-provider exchange-rate fallback.

## Payer Page

The Payer Page is hosted by `payaffe`.

Before Supported Currency selection, the Payer Page shows the available Payment Options.

After Supported Currency selection, the Payer Page shows:

- expected cryptocurrency amount,
- Payment Address,
- QR code for the payment instruction,
- Payment Expiration,
- current Payment status.

The Payer Page updates Payment status through polling.

The Payer Page must distinguish at least these user-facing states:

- waiting for payment,
- observed,
- completed,
- expired.

Payment Creation may include an optional Return URL. After completion, the Payer Page shows a link or button to the Return URL when one was provided.

The MVP does not automatically redirect the Payer after completion.

## Payment Expiration

Payments have a configurable Payment Expiration.

- The default Payment Expiration is one hour.
- Admins can configure the Payment Expiration for each Project.
- If the Payer does not complete the Payment before the configured expiration time, the Payment leaves the regular payment window.
- A Blockchain Transaction with an Observed Payment Time before Payment Expiration may still complete the Payment automatically even when it is confirmed after Payment Expiration.

## Late Payments

Payments have a configurable Late Acceptance Window after Payment Expiration.

- If a matching Blockchain Transaction has an Observed Payment Time after Payment Expiration but within the configured Late Acceptance Window, the Payment is accepted automatically.
- If a matching Blockchain Transaction has an Observed Payment Time after the Late Acceptance Window, the Payment is not accepted automatically.
- Admins may manually settle late Payments that were not accepted automatically.
- The default Late Acceptance Window is 24 hours.

Observed Payment Time is supplied by the configured Blockchain Truth when available. When the Blockchain Truth cannot provide a reliable transaction timestamp, `payaffe` uses the first observation time recorded by `payaffe`.

## Address And Custody Model

The MVP is non-custodial.

- `payaffe` does not hold Key Custody.
- `payaffe` must not store private keys, seed phrases, or other secrets that can spend received funds.
- `payaffe` does not perform refunds, sweeps, or withdrawals.
- Each Payment receives a Payment Address after the Payer selects a Supported Currency.
- For BTC and LTC, each Project uses configured Watch-Only Wallet Source bindings to derive Payment Addresses.
- For native ETH, each Project uses an imported Address Pool.
- Native ETH Payment Addresses are not automatically reused after assignment.
- Admins can bulk-import native ETH addresses.
- Admins can inspect the available native ETH Address Pool capacity.
- `payaffe` warns Admins when the available native ETH Address Pool falls below a configured threshold.
- If no unused native ETH Payment Address is available, native ETH is not offered as a Payment Option.

## Blockchain Observation Availability

`payaffe` tracks Observation Health per Supported Currency.

Payment Creation remains available when at least one Payment Option can still be offered.

A Payment Option is disabled on the Payer Page when Blockchain Observation for that Supported Currency is currently unavailable.

Existing active Payments remain valid when Blockchain Observation becomes unavailable after the Payer has selected a Supported Currency.

When Blockchain Observation is unavailable for an existing active Payment, the Payer Page shows that payment detection is delayed.

`payaffe` continues observation attempts and processes delayed observations when Blockchain Observation becomes available again.

The Admin UI shows Observation Health per Supported Currency.

Free Hosted Blockchain API tiers may be used in production when the operator accepts their operational risk.

The Admin UI shows relevant provider errors and rate-limit signals when available.

Product documentation must warn that free Hosted Blockchain API tiers may throttle requests, become unavailable, or change terms.

For production deployments where payment detection reliability matters, paid provider plans or sufficient request limits are recommended.

## Payment Completion

Matching Blockchain Transactions first make a Payment an Observed Payment.

An Observed Payment is completed only after the configured Confirmation Requirement for the selected Supported Currency is met. External systems are notified of successful completion only when the Payment is completed.

Confirmation Requirements are configurable per Project and Supported Currency.

Default Confirmation Requirements:

- BTC: 1 confirmation
- LTC: 1 confirmation
- native ETH: 12 confirmations

Payment completion is based on the sum of confirmed Matching Blockchain Transactions for the Payment.

- Multiple Matching Blockchain Transactions may complete one Payment together.
- Matching Blockchain Transactions must belong to the selected Supported Currency and payment address.
- Matching Blockchain Transactions must fall within the Payment Expiration or Late Acceptance Window rules.
- A later matching transaction may complete a Payment after an earlier underpaid transaction.

Underpayments are handled with a configurable Payment Tolerance per Project.

- Payment Tolerance is a percentage of the expected cryptocurrency amount.
- The default Payment Tolerance is 1 percent.
- Underpayments inside the configured tolerance are accepted automatically.
- Underpayments outside the configured tolerance are not accepted automatically.
- Admins may manually settle underpaid Payments outside the tolerance.

Overpayments are accepted automatically when the Confirmation Requirement is met.

- Overpayments are visible as Overpayments.
- The MVP does not provide automatic refund workflows for Overpayments.

## Payment Evidence Storage

`payaffe` stores enough payment evidence to explain why a Payment was observed, completed, settled, or alerted.

For each Matching Blockchain Transaction, `payaffe` stores at least:

- transaction hash,
- Supported Currency,
- Payment Address,
- observed amount,
- Observed Payment Time,
- first observed timestamp,
- current confirmation count,
- block hash when confirmed and available,
- block height when confirmed and available,
- raw provider or source identifier,
- last checked timestamp,
- whether the transaction contributed to Payment completion,
- reorg affected flag when relevant.

For each Payment, `payaffe` stores at least:

- expected cryptocurrency amount,
- selected Supported Currency,
- Rate Lock details,
- assigned Payment Address,
- observed total,
- confirmed eligible total,
- Payment Event History.

## Reorg Handling

Completed Payments are final for external systems.

If a blockchain reorganization affects a completed Payment, `payaffe` does not automatically move the Payment out of completed status.

Instead, `payaffe` creates a Reorg Alert and records the event in Payment Event History.

Admins review Reorg Alerts manually.

Reorg Monitoring Depth is configurable per Project and Supported Currency.

Default Reorg Monitoring Depth:

- BTC: 6 blocks
- LTC: 12 blocks
- native ETH: 64 blocks

## Integration Updates

External systems can learn about payment changes through both:

- webhooks,
- polling through the Integration API.

Polling through the Integration API is the recovery mechanism when webhooks are missed or delayed.

The Integration API also supports authenticated Currency Selection for an
Embedded Payment Flow. Its exact amounts, wallet URI rules, idempotent
concurrency behavior, and compatibility guarantees follow the
[Integration API contract](../architecture/integration-api-contract.md).

The supported `net10.0` client surface, target-product authorization duties,
polling backoff, and headless acceptance scenarios follow the
[Embedded Payment and .NET SDK baseline](../architecture/embedded-payment-sdk-baseline.md).

The MVP supports these webhook events:

- `payment.created`
- `payment.currency_selected`
- `payment.observed`
- `payment.completed`
- `payment.expired`
- `payment.settled`

Webhook Endpoints belong to Integration API Credentials and therefore to the
same Project. A Webhook Event is delivered only to endpoints in the Payment's
Project.

Each Integration API Credential may have zero or more Webhook Endpoints.

Each Webhook Endpoint has:

- URL,
- secret,
- status,
- event selection.

The default event selection for a Webhook Endpoint is all MVP webhook events.

Webhook Deliveries are signed with HMAC-SHA256 using a webhook secret.

Failed Webhook Deliveries are retried with backoff.

Admins can inspect Webhook Delivery history in the Admin UI and manually resend failed Webhook Deliveries.

## Integration API Authentication

The MVP uses static bearer tokens for Integration API authentication.

Admins can manage multiple Integration API Credentials per Project.

Each Integration API Credential has:

- Project,
- name,
- status,
- created timestamp,
- last used timestamp.

Integration API Credentials can be disabled.

Bearer tokens are shown only once when created. `payaffe` stores only a token hash.

The MVP does not use OAuth or JWT authentication for external systems.

## MVP Currency Support

- BTC
- LTC
- native ETH

## Admin MCP Scope

The MVP MCP is intended for use through an agent CLI such as Codex CLI or Claude Code CLI.

The MVP MCP is authenticated as an Admin surface but intentionally narrower than the Admin UI.
Its contract is recorded in
[../architecture/admin-mcp-contract.md](../architecture/admin-mcp-contract.md).

The MVP MCP provides read access for:

- Payments and statuses,
- configuration summary,
- Webhook Delivery history,
- Address Pool status,
- Audit Log.

The MVP MCP provides write access for:

- manual Settlement of a Payment,
- resending failed Webhook Deliveries,
- importing native ETH Address Pool entries.

MCP write actions are written to the Audit Log with the acting Admin Account or MCP credential.
Risky MCP actions need explicit client-side confirmation according to the
shared MCP standard.

The MVP MCP does not create Integration API Credentials, change Webhook Endpoints, or change system configuration.

## Admin Accounts And Authorization

The MVP supports multiple local Admin Accounts. Every Admin Account can
administer every Project; Selected Project context is a safety control, not an
authorization role.

Admins authenticate with a local password, required TOTP MFA, and a session
cookie.

The MVP does not have Admin roles. All Admin Accounts have the same permissions.

Administrative actions with payment or security impact are written to the Audit Log with the acting Admin Account.

The MVP does not support OIDC, SAML, LDAP, or external identity providers for Admin authentication.

The MVP does not provide Remembered Devices, `remember me`, or email-based
self-service password reset for local Admin Accounts.

The MVP Audit Log records Admin and security actions, not every system lifecycle event.
Security-relevant Audit Log entries, retention, access, export, data
minimization, rate limits, generic security responses, and account lockout
boundaries follow the Admin security baseline in
[../architecture/admin-security-baseline.md](../architecture/admin-security-baseline.md).

Required Audit Log entries include Admin authentication and session events,
MFA and recovery-code events, step-up decisions, security-relevant
authorization denials, Integration API Credential changes, Webhook Endpoint
changes, manual Settlement, manual Webhook Delivery resend, native ETH Address
Pool imports, security-relevant configuration changes, Audit Log access and
export, lockout and rate-limit decisions, and risky admin MCP write actions.

Payment lifecycle changes, such as automatic completion, are recorded in Payment Event History.

Webhook Delivery attempts and failures are recorded in Webhook Delivery history.

## Non-Goals For The MVP

- Running complete blockchain nodes as a default requirement.
- Optimizing for hundreds of payments per minute.
- Supporting Monero.
- Supporting token payments such as ERC-20 tokens.
- Splitting one Payment across multiple Supported Currencies.
- Providing a fully trustless blockchain verification setup.
- Supporting refunds.
- Automatically reusing native ETH Payment Addresses.
- OAuth or JWT authentication for external systems.
- OIDC, SAML, LDAP, or external identity providers for Admin authentication.

## Constraints

- The backend technology is .NET 10.
- Deployment is based on Docker Compose.
- The product is open source.
