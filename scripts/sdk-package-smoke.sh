#!/usr/bin/env bash
#
# Clean-consumer check for the Payaffe.Sdk package.
#
# Packs the SDK, inspects what the package actually contains, and then builds and
# runs a throwaway net10.0 application that consumes it the way an integrator
# does: `dotnet add package`, from a feed, outside this repository. That last part
# is the point. Inside the repository a project reference, Directory.Build.props
# and Directory.Packages.props quietly supply a target framework, a language
# version and every dependency version; a consumer has none of that, and a package
# that only builds here is a package that does not work.
#
# Usage: scripts/sdk-package-smoke.sh [--keep]
#
#   --keep  leave the consumer project behind for inspection
#
set -euo pipefail

KEEP="false"
[ "${1:-}" = "--keep" ] && KEEP="true"

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPOSITORY_ROOT"

# Outside the repository on purpose: a consumer inherits none of its build files.
WORK="$(mktemp -d -t payaffe-sdk-smoke.XXXXXX)"
FEED="$WORK/feed"
CONSUMER="$WORK/consumer"

FAILURES=0

pass() { printf '  \033[32mok\033[0m   %s\n' "$1"; }
fail() { FAILURES=$((FAILURES + 1)); printf '  \033[31mFAIL\033[0m %s\n' "$1"; }
section() { printf '\n\033[1m%s\033[0m\n' "$1"; }

cleanup() {
  if [ "$KEEP" = "true" ]; then
    printf '\nConsumer kept at %s\n' "$WORK"
  else
    rm -rf "$WORK"
  fi
}
trap cleanup EXIT

section "Pack"
mkdir -p "$FEED"
dotnet pack src/Payaffe.Sdk/Payaffe.Sdk.csproj -c Release -o "$FEED" >/dev/null
PACKAGE="$(ls "$FEED"/Payaffe.Sdk.*.nupkg | head -n 1)"
VERSION="$(basename "$PACKAGE" .nupkg)"
VERSION="${VERSION#Payaffe.Sdk.}"
pass "packed Payaffe.Sdk $VERSION"

section "What the package contains"
CONTENTS="$(unzip -Z1 "$PACKAGE")"

if printf '%s\n' "$CONTENTS" | grep -q '^lib/net10.0/Payaffe.Sdk.dll$'; then
  pass "carries lib/net10.0/Payaffe.Sdk.dll"
else
  fail "no lib/net10.0/Payaffe.Sdk.dll in the package"
fi

# One target framework. A second one would be a compatibility promise nobody
# decided to make.
FRAMEWORKS="$(printf '%s\n' "$CONTENTS" | grep '^lib/' | cut -d/ -f2 | sort -u)"
if [ "$FRAMEWORKS" = "net10.0" ]; then
  pass "targets net10.0 and nothing else"
else
  fail "unexpected target frameworks: $(printf '%s' "$FRAMEWORKS" | tr '\n' ' ')"
fi

if printf '%s\n' "$CONTENTS" | grep -q '^README.md$'; then
  pass "ships its README"
else
  fail "the package has no README"
fi

# Nothing from this project's private planning, and nothing that looks like a
# credential, may travel to nuget.org inside a package.
EXTRACTED="$WORK/extracted"
mkdir -p "$EXTRACTED"
unzip -q "$PACKAGE" -d "$EXTRACTED"
LEAKS="$(grep -rIlE 'PAY-[0-9]+|planaffe|BEGIN [A-Z ]*PRIVATE KEY|api[_-]?token[=:][[:space:]]*[A-Za-z0-9]' "$EXTRACTED" || true)"
if [ -z "$LEAKS" ]; then
  pass "no tracker references or credential-shaped strings"
else
  fail "the package carries something it should not: $LEAKS"
fi

section "A clean consumer"
mkdir -p "$CONSUMER"
cd "$CONSUMER"

cat > NuGet.config <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="../feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

dotnet new console -o . --force >/dev/null
dotnet add package Payaffe.Sdk --version "$VERSION" >/dev/null

cat > Program.cs <<'CSHARP'
// A consumer of the published package and nothing else: no project reference, no
// shared build files, no test framework. It touches every public surface the SDK
// promises and fails loudly if one of them is missing from the package.
using System.Security.Cryptography;
using System.Text;
using Payaffe.Sdk;

const string PaymentUri = "bitcoin:bc1qexampleaddress00000000000000000000000000?amount=0.00039980";

// The typed client, built the way a product without dependency injection builds it.
using HttpClient httpClient = new();
PayaffeClient client = new(httpClient, new Uri("https://payaffe.example.test"), "integration-token");
if (client is null)
{
    throw new InvalidOperationException("The client could not be constructed.");
}

// The QR helper, including the dependency it brings with it.
PayaffePaymentQrCode code = PayaffePaymentQrCode.CreateForUri(PaymentUri);
string svg = code.ToSvg();
if (code.Payload != PaymentUri || !svg.StartsWith("<svg", StringComparison.Ordinal) || code.ModuleCount < 21)
{
    throw new InvalidOperationException("The QR helper did not produce the payment code.");
}

// The webhook verifier, against a delivery signed here.
long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
string body = """
{"event_id":"0198b2a1-0000-7000-8000-000000000001","event_type":"payment.completed","event_version":"2026-01-01","occurred_at":"2026-01-01T00:00:00Z","correlation_id":null,"resource":null,"payment":{"payment_id":"0198b2a1-0000-7000-8000-000000000002","external_reference":"order-1","status":"completed","fiat_currency":"EUR","fiat_amount_minor":1999,"selected_currency":"BTC","expected_crypto_amount":"0.00039980","expected_crypto_amount_atomic":"39980","observed_total":"0.00039980","confirmed_eligible_total":"0.00039980","observed_amount_state":"exact","payer_page_id":null,"expires_at":"2026-01-01T00:30:00Z","completed_at":"2026-01-01T00:10:00Z","settled_at":null}}
""";
string signature = Convert.ToHexStringLower(HMACSHA256.HashData(
    Encoding.UTF8.GetBytes("webhook-secret"),
    Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

PayaffeWebhookVerificationResult verified = PayaffeWebhookVerifier.Verify(
    Encoding.UTF8.GetBytes(body),
    new PayaffeWebhookHeaders(null, timestamp.ToString(), $"v1={signature}"),
    "webhook-secret",
    DateTimeOffset.UtcNow);
if (!verified.IsValid || verified.Event!.Payment.Status != PaymentStatus.Completed)
{
    throw new InvalidOperationException("A signed delivery did not verify.");
}

PayaffeWebhookVerificationResult tampered = PayaffeWebhookVerifier.Verify(
    Encoding.UTF8.GetBytes(body.Replace("1999", "1", StringComparison.Ordinal)),
    new PayaffeWebhookHeaders(null, timestamp.ToString(), $"v1={signature}"),
    "webhook-secret",
    DateTimeOffset.UtcNow);
if (tampered.IsValid)
{
    throw new InvalidOperationException("A tampered delivery verified.");
}

Console.WriteLine("Payaffe.Sdk works from a clean consumer.");
CSHARP

if dotnet run --configuration Release > "$WORK/consumer.log" 2>&1; then
  pass "$(tail -n 1 "$WORK/consumer.log")"
else
  fail "the consumer did not build or run"
  sed 's/^/       /' "$WORK/consumer.log"
fi

section "Result"
if [ "$FAILURES" -eq 0 ]; then
  printf '  \033[32mPayaffe.Sdk %s is consumable.\033[0m\n\n' "$VERSION"
else
  printf '  \033[31m%s check(s) failed.\033[0m\n\n' "$FAILURES"
  exit 1
fi
