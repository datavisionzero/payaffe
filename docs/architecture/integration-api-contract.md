# Integration API Contract

This document records the target contract baseline for the public Integration
API. It complements the product requirements and ADRs; generated OpenAPI and
tests become the technical source of truth once implementation exists.

## Scope

The Integration API is the stable HTTP API used by external systems to create
and inspect Payments.

It is separate from:

- Admin UI backend routes,
- Payer Page backend routes,
- admin MCP tools,
- outgoing Webhook Delivery payloads.

## Versioning

The MVP Integration API uses path-based major versioning:

- `/api/v1/...`

Breaking changes require a new major path such as `/api/v2/...`. Minor and
patch changes are represented through backward-compatible schema and behavior
changes, not through path changes.

The first public resource paths are:

- `POST /api/v1/payments`
- `GET /api/v1/payments/{paymentId}`

Additional Integration API endpoints must be designed as part of the public
contract before implementation.

## Authentication

Integration API requests use static bearer tokens issued through Integration
API Credentials.

The Integration API is not cookie-authenticated and does not use CSRF tokens.
Browser-facing Admin routes have their own CSRF requirements.

Authentication failures use generic responses that do not disclose whether a
token exists, is disabled, or has the wrong shape unless the detail is safe for
the caller.

Integration API requests use a fixed-window default rate limit of 120 requests
per minute for each route, source IP, and bearer-token fingerprint partition.
Requests without bearer-token evidence are partitioned by route and source IP.
The bearer token itself must not be stored in the rate-limit key, logs, Audit
Log entries, traces, metrics, or error responses.

## Payment Creation

`POST /api/v1/payments` creates a Payment.

The request must include:

- Fiat Amount,
- External Reference,
- `Idempotency-Key` header.

The request may include:

- Payment Context Fields,
- Return URL.

The response includes at least:

- Payment identifier,
- current Payment status,
- Payer Page URL,
- Payment Expiration.

Payment Creation idempotency is scoped to the Integration API Credential:

- the same `Idempotency-Key` with the same Payment Creation data returns the
  same Payment,
- the same `Idempotency-Key` with different Payment Creation data returns an
  idempotency conflict error.

## Payment Polling

`GET /api/v1/payments/{paymentId}` returns the current Payment state for
external-system polling and reconciliation.

The response includes the data needed by an external system to decide whether
the Payment is still waiting, observed, completed, expired, settled, or
otherwise requires manual follow-up according to the product requirements.

Polling is the recovery path when Webhook Deliveries are missed or delayed.

## Errors

Errors use `ProblemDetails`.

Error responses include safe standard fields and should include:

- `correlationId`,
- stable machine-readable `code` when the client can act on the error.

The initial public error-code set should include at least:

- `authentication.required`,
- `authentication.invalid`,
- `authorization.denied`,
- `validation.failed`,
- `payment.not_found`,
- `idempotency.conflict`,
- `payment_options.unavailable`,
- `rate_limited`,
- `unexpected_error`.

Validation errors use `validation.failed` with an `errors` extension. The
extension maps field paths to lists of machine-readable validation codes.
Validation codes are API contract values, not localized UI strings.

## Pagination, Sorting, And Filtering

The MVP does not require a list endpoint for Integration API polling.

If a future Integration API list endpoint is added, it should use cursor-based
pagination by default with:

- `limit`,
- `cursor`,
- `sort`,
- explicitly documented filter parameters.

Free sorting or filtering over arbitrary database, entity, or DTO fields is not
allowed.

## Optimistic Concurrency

The initial Integration API exposes create/read endpoints and does not require
`ETag` or `If-Match`.

Future Integration API mutation endpoints must decide whether lost updates or
stale client state can cause incorrect behavior. If they can, the endpoint
must use opaque `ETag` values and require `If-Match` on writes.

## OpenAPI And Contract Diff

The ASP.NET Core implementation must generate OpenAPI from the implemented API.
OpenAPI is not maintained manually as a second technical truth.

The generated Integration API description must expose:

- versioned paths,
- request and response schemas,
- `ProblemDetails` responses,
- validation error structure,
- bearer-token authentication requirements,
- required idempotency headers,
- pagination, sorting, and filtering parameters when list endpoints exist,
- `ETag` and `If-Match` headers when concurrency-controlled mutations exist.

Once implementation exists, CI must:

- verify that OpenAPI generation succeeds,
- compare the generated OpenAPI document against the last accepted contract
  snapshot,
- require a visible major-version decision for breaking changes.

Accepted snapshot storage and tooling are created only when the API
implementation exists.
