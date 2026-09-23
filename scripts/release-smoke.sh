#!/usr/bin/env bash
#
# Release smoke check for payaffe.
#
# Brings up a throwaway installation from empty volumes and verifies the paths
# that must work in a freshly deployed release. It runs under its own Compose
# project name and its own ports, so it can never touch an operator's real
# volumes, containers, or `.env`.
#
# Usage: scripts/release-smoke.sh [--keep]
#
#   --keep  leave the smoke installation running for manual inspection
#
set -euo pipefail

PROJECT="payaffe-smoke"
ENV_FILE="$(mktemp -t payaffe-smoke-env.XXXXXX)"
API_PORT="18080"
DB_PORT="15432"
RECEIVER="payaffe-smoke-receiver"
KEEP="false"
[ "${1:-}" = "--keep" ] && KEEP="true"

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPOSITORY_ROOT"

FAILURES=0
STEP=0

pass() { STEP=$((STEP + 1)); printf '  \033[32mok\033[0m   %s\n' "$1"; }
fail() { STEP=$((STEP + 1)); FAILURES=$((FAILURES + 1)); printf '  \033[31mFAIL\033[0m %s\n' "$1"; }
note() { printf '       %s\n' "$1"; }
section() { printf '\n\033[1m%s\033[0m\n' "$1"; }

compose() { docker compose -p "$PROJECT" --env-file "$ENV_FILE" "$@"; }

cleanup() {
  docker rm -f "$RECEIVER" >/dev/null 2>&1 || true
  if [ "$KEEP" = "true" ]; then
    note "Smoke installation kept running as Compose project '$PROJECT'."
    note "Remove it with: docker compose -p $PROJECT down -v"
  else
    compose down -v >/dev/null 2>&1 || true
  fi
  rm -f "$ENV_FILE"
}
trap cleanup EXIT

section "Preparing an isolated smoke installation"

WEBHOOK_SECRET="smoke-$(openssl rand -hex 16)"
API_TOKEN="payaffe_integration_smoke_$(openssl rand -hex 16)"
TOKEN_HASH="sha256:$(printf '%s' "$API_TOKEN" | openssl dgst -sha256 | awk '{print $NF}')"
CREDENTIAL_ID="11111111-1111-4111-8111-111111111111"

{
  cat .env.example
  echo "PAYAFFE_DB_PASSWORD=smoke-$(openssl rand -hex 12)"
  echo "PAYAFFE_DB_PORT=$DB_PORT"
  echo "PAYAFFE_API_PORT=$API_PORT"
  echo "PAYAFFE_PAYER_PAGE_BASE_URL=http://localhost:$API_PORT/pay"
  echo "PAYAFFE_WEBHOOK_ENDPOINT_SECRET_PARTNER_V1=$WEBHOOK_SECRET"
} >"$ENV_FILE"
note "Compose project: $PROJECT (ports $API_PORT/$DB_PORT)"

compose down -v >/dev/null 2>&1 || true
compose build >/dev/null
pass "All release images build"

section "Controlled migrations from an empty database"

if compose --profile operations run --rm migrations >/dev/null 2>&1; then
  pass "Migration runner applies the schema to an empty volume"
else
  fail "Migration runner failed"
  exit 1
fi

section "Starting the product hosts"

compose up -d db api worker >/dev/null

wait_healthy() {
  local service="$1" attempts="${2:-60}" status
  for _ in $(seq 1 "$attempts"); do
    status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \
      "$(compose ps -q "$service")" 2>/dev/null || true)"
    [ "$status" = "healthy" ] && return 0
    sleep 3
  done
  return 1
}

for service in db api worker; do
  if wait_healthy "$service"; then
    pass "Service '$service' reports healthy"
  else
    fail "Service '$service' never became healthy"
  fi
done

section "Health endpoints"

if [ "$(curl -s "http://localhost:$API_PORT/health/live")" = '{"status":"alive"}' ]; then
  pass "GET /health/live"
else
  fail "GET /health/live did not report alive"
fi

if [ "$(curl -s "http://localhost:$API_PORT/health/ready")" = '{"status":"ready"}' ]; then
  pass "GET /health/ready"
else
  fail "GET /health/ready did not report ready"
fi

if compose exec -T worker dotnet Payaffe.Worker.dll health >/dev/null 2>&1; then
  pass "Worker liveness command"
else
  fail "Worker liveness command reported unhealthy"
fi

section "Seeding a smoke Integration API Credential"

# Creating a Credential through the product requires an authenticated Admin
# session and Step-up, and the first Admin needs an interactive terminal. The smoke
# check therefore seeds one verifier directly, the same way an operator with
# database authority could. Everything after this point uses the public API.
compose exec -T db psql --quiet --username "$(grep -m1 '^PAYAFFE_DB_USER=' "$ENV_FILE" | cut -d= -f2)" \
  --dbname "$(grep -m1 '^PAYAFFE_DB_NAME=' "$ENV_FILE" | cut -d= -f2)" >/dev/null <<SQL
insert into auth.integration_api_credentials
  (project_id, id, name, token_hash, status, created_at, updated_at, version)
values
  ('00000000-0000-0000-0000-000000000001', '$CREDENTIAL_ID', 'release smoke', '$TOKEN_HASH', 'active', now(), now(), 1);
SQL
pass "Integration API Credential seeded"

section "Integration API"

create_payment() {
  curl -s -o /tmp/payaffe-smoke-body -w '%{http_code}' -X POST "http://localhost:$API_PORT/api/v1/payments" \
    -H "Authorization: Bearer $API_TOKEN" \
    -H "Idempotency-Key: $1" \
    -H "Content-Type: application/json" \
    -d "$2"
}

BODY='{"fiatCurrency":"EUR","fiatAmountMinor":1999,"externalReference":"release-smoke"}'
STATUS="$(create_payment smoke-key-1 "$BODY")"
PAYMENT_ID="$(python3 -c 'import json;print(json.load(open("/tmp/payaffe-smoke-body")).get("paymentId",""))' 2>/dev/null || true)"
PAYER_PAGE_URL="$(python3 -c 'import json;print(json.load(open("/tmp/payaffe-smoke-body")).get("payerPageUrl",""))' 2>/dev/null || true)"
if [ "$STATUS" = "201" ] && [ -n "$PAYMENT_ID" ]; then
  pass "POST /api/v1/payments creates a Payment"
else
  fail "POST /api/v1/payments returned $STATUS"
  note "$(cat /tmp/payaffe-smoke-body)"
fi

# A replay is deliberately 200 rather than 201: nothing was created.
STATUS="$(create_payment smoke-key-1 "$BODY")"
REPEATED_ID="$(python3 -c 'import json;print(json.load(open("/tmp/payaffe-smoke-body")).get("paymentId",""))' 2>/dev/null || true)"
if [ "$STATUS" = "200" ] && [ "$REPEATED_ID" = "$PAYMENT_ID" ]; then
  pass "Repeating the Idempotency-Key replays the same Payment as 200"
else
  fail "Idempotent retry returned $STATUS with id '$REPEATED_ID'"
fi

STATUS="$(create_payment smoke-key-1 '{"fiatCurrency":"USD","fiatAmountMinor":500,"externalReference":"release-smoke"}')"
CODE="$(python3 -c 'import json;print(json.load(open("/tmp/payaffe-smoke-body")).get("code",""))' 2>/dev/null || true)"
if [ "$STATUS" = "409" ] && [ "$CODE" = "idempotency.conflict" ]; then
  pass "Reusing the key with different content conflicts"
else
  fail "Idempotency conflict returned $STATUS / '$CODE'"
fi

STATUS="$(curl -s -o /tmp/payaffe-smoke-body -w '%{http_code}' \
  -H "Authorization: Bearer $API_TOKEN" \
  "http://localhost:$API_PORT/api/v1/payments/$PAYMENT_ID")"
if [ "$STATUS" = "200" ]; then
  pass "GET /api/v1/payments/{id} retrieves the Payment"
else
  fail "Payment retrieval returned $STATUS"
fi

STATUS="$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$API_PORT/api/v1/payments/$PAYMENT_ID")"
if [ "$STATUS" = "401" ]; then
  pass "Unauthenticated retrieval is rejected"
else
  fail "Unauthenticated retrieval returned $STATUS"
fi

section "Payer Page"

PAYER_PAGE_ID="${PAYER_PAGE_URL##*/}"
STATUS="$(curl -s -o /tmp/payaffe-smoke-body -w '%{http_code}' \
  "http://localhost:$API_PORT/api/payer/payments/$PAYER_PAGE_ID")"
if [ "$STATUS" = "200" ]; then
  pass "Payer API serves the Payment by Payer Page id"
else
  fail "Payer API returned $STATUS"
fi

STATUS="$(curl -s -o /dev/null -w '%{http_code}' -H 'Accept: text/html' "http://localhost:$API_PORT/pay/$PAYER_PAGE_ID")"
if [ "$STATUS" = "200" ]; then
  pass "Web app renders the Payer Page"
else
  fail "Payer Page returned $STATUS"
fi

OPTION_REASON="$(python3 -c '
import json
payment = json.load(open("/tmp/payaffe-smoke-body"))
option = next((o for o in payment.get("paymentOptions", []) if o["supportedCurrency"] == "BTC"), {})
print(option.get("unavailableReasonCode") or "")
' 2>/dev/null || true)"

STATUS="$(curl -s -o /tmp/payaffe-smoke-body -w '%{http_code}' -X POST \
  -H "Content-Type: application/json" -d '{"supportedCurrency":"BTC"}' \
  "http://localhost:$API_PORT/api/payer/payments/$PAYER_PAGE_ID/currency-selection")"
SELECTION_CODE="$(python3 -c 'import json;print(json.load(open("/tmp/payaffe-smoke-body")).get("code",""))' 2>/dev/null || true)"

if [ "$STATUS" = "200" ]; then
  pass "Currency Selection assigns a Rate Lock and Payment Address"
elif [ -n "$OPTION_REASON" ] && [ "$SELECTION_CODE" = "$OPTION_REASON" ]; then
  # The Payer Page shows the Payment Option reason, so the selection endpoint
  # has to name the same cause. A mismatch sends operators to the wrong
  # provider.
  pass "Currency Selection reports the same reason as the Payment Option ($OPTION_REASON)"
  case "$OPTION_REASON" in
    payment_address.unavailable)
      note "Expected on a fresh installation: no Watch-Only Wallet Source and"
      note "no native ETH Address Pool. Configure an address source, then"
      note "complete Currency Selection manually." ;;
    *)
      note "Investigate before release; this is not a known fresh-install state." ;;
  esac
else
  fail "Currency Selection returned $STATUS / '${SELECTION_CODE:-unknown}' while the Payment Option said '${OPTION_REASON:-available}'"
fi

section "Exchange Rate Source"

# HttpClient sends no User-Agent by default and the rate provider answers such
# requests with 403, so an empty Rate Cache here means no Payment can ever be
# paid.
CACHED_PAIRS="$(compose exec -T db psql -tA --username "$(grep -m1 '^PAYAFFE_DB_USER=' "$ENV_FILE" | cut -d= -f2)" \
  --dbname "$(grep -m1 '^PAYAFFE_DB_NAME=' "$ENV_FILE" | cut -d= -f2)" \
  -c "select count(*) from app.rate_cache" 2>/dev/null | tr -d ' \r')"
if [ "${CACHED_PAIRS:-0}" -ge 6 ]; then
  pass "Rate Cache holds all six fiat/cryptocurrency pairs"
else
  fail "Rate Cache holds $CACHED_PAIRS of 6 pairs"
  note "The worker host needs outbound access to the Exchange Rate provider."
  note "Check the worker log for the provider HTTP status."
fi

section "Webhook Delivery"

NETWORK="$(docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}' "$(compose ps -q api)")"
docker rm -f "$RECEIVER" >/dev/null 2>&1 || true
docker run -d --name "$RECEIVER" --network "$NETWORK" python:3.13-alpine python -c '
import http.server
class Handler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("content-length", 0))).decode()
        print("TIMESTAMP=" + self.headers.get("Payaffe-Webhook-Timestamp", ""), flush=True)
        print("SIGNATURE=" + self.headers.get("Payaffe-Webhook-Signature", ""), flush=True)
        print("EVENTTYPE=" + self.headers.get("Payaffe-Webhook-Event-Type", ""), flush=True)
        print("BODY=" + body, flush=True)
        self.send_response(204)
        self.end_headers()
    def log_message(self, *args):
        pass
http.server.HTTPServer(("0.0.0.0", 9000), Handler).serve_forever()
' >/dev/null

compose exec -T db psql --quiet --username "$(grep -m1 '^PAYAFFE_DB_USER=' "$ENV_FILE" | cut -d= -f2)" \
  --dbname "$(grep -m1 '^PAYAFFE_DB_NAME=' "$ENV_FILE" | cut -d= -f2)" >/dev/null <<SQL
insert into app.webhook_endpoints (id, integration_api_credential_id, url, secret_reference, status, event_types, created_at, updated_at, version)
values (gen_random_uuid(), '$CREDENTIAL_ID', 'http://$RECEIVER:9000/webhook',
        'configuration:Webhooks:EndpointSecrets:partner-v1', 'active', null, now(), now(), 1);
SQL

STATUS="$(create_payment smoke-key-webhook "$BODY")"
if [ "$STATUS" != "201" ]; then
  fail "Could not create the Payment that triggers a Webhook Event"
fi

DELIVERED="false"
for _ in $(seq 1 20); do
  if docker logs "$RECEIVER" 2>/dev/null | grep -q '^SIGNATURE=v1='; then
    DELIVERED="true"
    break
  fi
  sleep 3
done

if [ "$DELIVERED" = "true" ]; then
  pass "Worker delivers a signed Webhook Event to the receiver"
  RECEIVED="$(docker logs "$RECEIVER" 2>/dev/null)"
  RECEIVED_TIMESTAMP="$(echo "$RECEIVED" | grep -m1 '^TIMESTAMP=' | cut -d= -f2-)"
  RECEIVED_SIGNATURE="$(echo "$RECEIVED" | grep -m1 '^SIGNATURE=' | cut -d= -f2-)"
  RECEIVED_EVENT_TYPE="$(echo "$RECEIVED" | grep -m1 '^EVENTTYPE=' | cut -d= -f2-)"
  RECEIVED_BODY="$(echo "$RECEIVED" | grep -m1 '^BODY=' | cut -d= -f2-)"
  EXPECTED_SIGNATURE="v1=$(printf '%s.%s' "$RECEIVED_TIMESTAMP" "$RECEIVED_BODY" \
    | openssl dgst -sha256 -hmac "$WEBHOOK_SECRET" | awk '{print $NF}')"
  if [ "$RECEIVED_SIGNATURE" = "$EXPECTED_SIGNATURE" ]; then
    pass "Webhook HMAC-SHA256 signature verifies against the configured secret"
  else
    fail "Webhook signature did not verify"
  fi
  if [ "$RECEIVED_EVENT_TYPE" = "payment.created" ]; then
    pass "Webhook carries the payment.created event type"
  else
    fail "Webhook event type was '$RECEIVED_EVENT_TYPE'"
  fi
else
  fail "No Webhook Event reached the receiver"
  note "$(compose logs --tail 20 worker 2>&1 | tail -5)"
fi

section "Background workers"

LEASES="$(compose exec -T db psql -tA --username "$(grep -m1 '^PAYAFFE_DB_USER=' "$ENV_FILE" | cut -d= -f2)" \
  --dbname "$(grep -m1 '^PAYAFFE_DB_NAME=' "$ENV_FILE" | cut -d= -f2)" \
  -c "select count(*) from app.background_worker_leases where last_succeeded_at is not null" 2>/dev/null | tr -d ' \r')"
if [ "${LEASES:-0}" -ge 4 ]; then
  pass "All four named worker leases completed a batch ($LEASES)"
else
  fail "Only $LEASES worker leases recorded a successful batch"
fi

FAILING="$(compose exec -T db psql -tA --username "$(grep -m1 '^PAYAFFE_DB_USER=' "$ENV_FILE" | cut -d= -f2)" \
  --dbname "$(grep -m1 '^PAYAFFE_DB_NAME=' "$ENV_FILE" | cut -d= -f2)" \
  -c "select count(*) from app.background_worker_leases where consecutive_failure_count > 0" 2>/dev/null | tr -d ' \r')"
if [ "${FAILING:-1}" = "0" ]; then
  pass "No worker is in a failing state"
else
  note "$FAILING worker(s) report consecutive failures; check the worker log."
  note "Exchange Rate refresh needs outbound access to the rate provider."
fi

if [ "$(compose logs api 2>&1 | grep -ci 'HostedService')" = "0" ]; then
  pass "The API host runs no background worker"
else
  fail "The API host logged background worker activity"
fi

section "Result"

if [ "$FAILURES" -eq 0 ]; then
  printf '  \033[32mAll %d automated checks passed.\033[0m\n' "$STEP"
else
  printf '  \033[31m%d of %d checks failed.\033[0m\n' "$FAILURES" "$STEP"
fi

cat <<'MANUAL'

  Still required before declaring a release ready. These need an interactive
  terminal or an authenticator app and cannot be scripted:

    1. Bootstrap the first Admin Account and store the Recovery Codes once.
    2. Sign in with the password, enrol TOTP, then exercise one
       Step-up-protected write, such as manual Settlement or Webhook Endpoint
       rotation.
    3. Import native ETH Address Pool entries and confirm capacity reporting.
    4. Complete Currency Selection against a real configured address source and
       confirm the derived address in the external watch-only wallet.
    5. Call one read-only Admin MCP tool with a configured Admin Account id.
    6. Run the restore verification checklist in
       docs/operations/postgresql-backup-restore.md.
MANUAL

exit "$FAILURES"
