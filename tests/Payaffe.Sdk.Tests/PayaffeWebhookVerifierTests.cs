using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Payaffe.Sdk.Tests;

public sealed class PayaffeWebhookVerifierTests
{
    private const string _secret = "webhook-secret";

    private static readonly DateTimeOffset _signedAt =
        new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private const string _completedPayload = """
        {
          "event_id": "0fd2b1c4-9a0e-4a3c-8f64-2b0a5c9f4d11",
          "event_type": "payment.completed",
          "event_version": "1",
          "occurred_at": "2026-07-04T12:00:00+00:00",
          "correlation_id": "6f3d9c2a-1b27-4f0e-9d3a-7c9b1e5a2f80",
          "resource": { "type": "payment", "id": "3b6f0a52-6f4e-4f09-a0c9-1d2f6b7c8e90" },
          "payment": {
            "payment_id": "3b6f0a52-6f4e-4f09-a0c9-1d2f6b7c8e90",
            "external_reference": "order-123",
            "status": "completed",
            "fiat_currency": "EUR",
            "fiat_amount_minor": 1999,
            "selected_currency": "BTC",
            "expected_crypto_amount": "0.00039980",
            "expected_crypto_amount_atomic": "39980",
            "observed_total": "0.0003998",
            "confirmed_eligible_total": "0.0003998",
            "observed_amount_state": "exact",
            "payer_page_id": "fixed-payer-page-id",
            "expires_at": "2026-07-04T13:00:00+00:00",
            "completed_at": "2026-07-04T12:00:00+00:00"
          }
        }
        """;

    [Fact]
    public void Signed_delivery_verifies_and_exposes_the_envelope()
    {
        byte[] body = Encoding.UTF8.GetBytes(_completedPayload);

        PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
            body,
            CreateHeaders(body),
            _secret,
            _signedAt.AddSeconds(30));

        Assert.True(result.IsValid);
        Assert.Null(result.RejectionReason);
        PayaffeWebhookEvent verified = Assert.IsType<PayaffeWebhookEvent>(result.Event);
        Assert.Equal(Guid.Parse("0fd2b1c4-9a0e-4a3c-8f64-2b0a5c9f4d11"), verified.EventId);
        Assert.Equal(PayaffeWebhookEventTypes.PaymentCompleted, verified.EventType);
        Assert.Equal("1", verified.EventVersion);
        Assert.Equal(_signedAt, verified.OccurredAt);
        Assert.Equal("payment", verified.Resource?.Type);
        Assert.Equal(PaymentStatus.Completed, verified.Payment.Status);
        Assert.Equal(SupportedCurrency.Btc, verified.Payment.SelectedCurrency);
        Assert.Equal(1999, verified.Payment.FiatAmountMinor);
        Assert.Equal("0.00039980", verified.Payment.ExpectedCryptoAmount);
        Assert.Equal("39980", verified.Payment.ExpectedCryptoAmountAtomic);
        Assert.Equal("exact", verified.Payment.ObservedAmountState);
        Assert.Equal(_signedAt, verified.Payment.CompletedAt);
        Assert.Null(verified.Payment.SettledAt);
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            result.DeliveryId);
    }

    [Fact]
    public void Tampered_body_is_rejected_and_exposes_no_event()
    {
        byte[] signedBody = Encoding.UTF8.GetBytes(_completedPayload);
        PayaffeWebhookHeaders headers = CreateHeaders(signedBody);
        byte[] tamperedBody = Encoding.UTF8.GetBytes(
            _completedPayload.Replace("\"fiat_amount_minor\": 1999", "\"fiat_amount_minor\": 1", StringComparison.Ordinal));

        PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
            tamperedBody,
            headers,
            _secret,
            _signedAt);

        Assert.False(result.IsValid);
        Assert.Equal(PayaffeWebhookRejectionReason.SignatureMismatch, result.RejectionReason);
        Assert.Null(result.Event);
    }

    [Fact]
    public void Another_endpoints_secret_does_not_verify_a_delivery()
    {
        byte[] body = Encoding.UTF8.GetBytes(_completedPayload);

        PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
            body,
            CreateHeaders(body),
            "a-different-endpoint-secret",
            _signedAt);

        Assert.Equal(PayaffeWebhookRejectionReason.SignatureMismatch, result.RejectionReason);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(6, false)]
    [InlineData(-6, false)]
    public void The_replay_window_bounds_both_a_stale_and_a_future_delivery(
        int minutesLate,
        bool expectedValid)
    {
        byte[] body = Encoding.UTF8.GetBytes(_completedPayload);

        PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
            body,
            CreateHeaders(body),
            _secret,
            _signedAt.AddMinutes(minutesLate));

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Equal(
                PayaffeWebhookRejectionReason.TimestampOutsideWindow,
                result.RejectionReason);
            Assert.Null(result.Event);
        }
    }

    [Fact]
    public void A_narrower_local_window_can_reject_a_delivery_the_default_window_accepts()
    {
        byte[] body = Encoding.UTF8.GetBytes(_completedPayload);
        PayaffeWebhookHeaders headers = CreateHeaders(body);
        DateTimeOffset now = _signedAt.AddMinutes(2);

        Assert.True(PayaffeWebhookVerifier.Verify(body, headers, _secret, now).IsValid);
        Assert.Equal(
            PayaffeWebhookRejectionReason.TimestampOutsideWindow,
            PayaffeWebhookVerifier
                .Verify(body, headers, _secret, now, TimeSpan.FromSeconds(30))
                .RejectionReason);
    }

    [Theory]
    [InlineData(null, "v1=00", PayaffeWebhookRejectionReason.MissingHeader)]
    [InlineData("1783166400", null, PayaffeWebhookRejectionReason.MissingHeader)]
    [InlineData("not-a-timestamp", "v1=00", PayaffeWebhookRejectionReason.MalformedTimestamp)]
    [InlineData("1783166400", "v1=not-hex", PayaffeWebhookRejectionReason.MalformedSignature)]
    [InlineData("1783166400", "sha256=abc", PayaffeWebhookRejectionReason.MalformedSignature)]
    public void Unusable_headers_are_rejected_before_any_payload_is_read(
        string? timestamp,
        string? signature,
        PayaffeWebhookRejectionReason expected)
    {
        byte[] body = Encoding.UTF8.GetBytes(_completedPayload);

        PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
            body,
            new PayaffeWebhookHeaders(
                Guid.Empty.ToString("D"),
                timestamp,
                signature),
            _secret,
            DateTimeOffset.FromUnixTimeSeconds(1783166400));

        Assert.Equal(expected, result.RejectionReason);
        Assert.Null(result.Event);
    }

    [Fact]
    public void A_signature_list_verifies_when_one_candidate_matches()
    {
        byte[] body = Encoding.UTF8.GetBytes(_completedPayload);
        string rotated = new('a', 64);
        PayaffeWebhookHeaders headers = CreateHeaders(body) with
        {
            Signature = $"v1={rotated}, {CreateSignature(body, _signedAt)}",
        };

        Assert.True(PayaffeWebhookVerifier.Verify(body, headers, _secret, _signedAt).IsValid);
    }

    [Fact]
    public void A_signed_but_unreadable_payload_is_rejected_as_malformed()
    {
        byte[] body = Encoding.UTF8.GetBytes("{\"event_id\": ");

        PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
            body,
            CreateHeaders(body),
            _secret,
            _signedAt);

        Assert.Equal(PayaffeWebhookRejectionReason.MalformedPayload, result.RejectionReason);
    }

    [Fact]
    public void Unknown_future_values_survive_verification()
    {
        byte[] body = Encoding.UTF8.GetBytes(
            _completedPayload
                .Replace("\"status\": \"completed\"", "\"status\": \"partially_settled\"", StringComparison.Ordinal)
                .Replace("\"selected_currency\": \"BTC\"", "\"selected_currency\": \"XMR\"", StringComparison.Ordinal)
                .Replace("\"event_type\": \"payment.completed\"", "\"event_type\": \"payment.partially_settled\"", StringComparison.Ordinal));

        PayaffeWebhookVerificationResult result = PayaffeWebhookVerifier.Verify(
            body,
            CreateHeaders(body),
            _secret,
            _signedAt);

        Assert.True(result.IsValid);
        Assert.Equal("payment.partially_settled", result.Event!.EventType);
        Assert.Equal("partially_settled", result.Event.Payment.Status.Value);
        Assert.False(result.Event.Payment.Status.IsTerminal);
        Assert.Equal("XMR", result.Event.Payment.SelectedCurrency?.Value);
    }

    [Fact]
    public void A_missing_secret_is_a_caller_defect_not_a_rejected_delivery()
    {
        byte[] body = Encoding.UTF8.GetBytes(_completedPayload);
        PayaffeWebhookHeaders headers = CreateHeaders(body);

        Assert.Throws<ArgumentException>(() =>
            PayaffeWebhookVerifier.Verify(body, headers, string.Empty, _signedAt));
    }

    [Fact]
    public void Headers_are_read_through_a_framework_independent_lookup()
    {
        Dictionary<string, string> raw = new(StringComparer.OrdinalIgnoreCase)
        {
            [PayaffeWebhookHeaderNames.DeliveryId] = "11111111-1111-1111-1111-111111111111",
            [PayaffeWebhookHeaderNames.Timestamp] = "1783166400",
            [PayaffeWebhookHeaderNames.Signature] = "v1=abc",
            [PayaffeWebhookHeaderNames.EventType] = PayaffeWebhookEventTypes.PaymentObserved,
            [PayaffeWebhookHeaderNames.EventVersion] = "1",
        };

        PayaffeWebhookHeaders headers = PayaffeWebhookHeaders.FromLookup(
            name => raw.TryGetValue(name, out string? value) ? value : null);

        Assert.Equal("11111111-1111-1111-1111-111111111111", headers.DeliveryId);
        Assert.Equal("1783166400", headers.Timestamp);
        Assert.Equal("v1=abc", headers.Signature);
        Assert.Equal(PayaffeWebhookEventTypes.PaymentObserved, headers.EventType);
        Assert.Equal("1", headers.EventVersion);
    }

    private static PayaffeWebhookHeaders CreateHeaders(byte[] body) => new(
        "11111111-1111-1111-1111-111111111111",
        _signedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        CreateSignature(body, _signedAt),
        PayaffeWebhookEventTypes.PaymentCompleted,
        "1");

    private static string CreateSignature(byte[] body, DateTimeOffset timestamp)
    {
        byte[] signatureBase = Encoding.UTF8.GetBytes(
            $"{timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}.{Encoding.UTF8.GetString(body)}");
        byte[] signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_secret), signatureBase);
        return "v1=" + Convert.ToHexString(signature).ToLowerInvariant();
    }
}
