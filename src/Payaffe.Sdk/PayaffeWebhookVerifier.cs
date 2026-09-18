using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payaffe.Sdk;

/// <summary>
/// Verifies the signature and replay window of an incoming Payaffe Webhook Delivery.
/// The verifier does not host an endpoint, choose a web framework, persist deduplication
/// state, or dispatch handlers: the receiving application owns all of that.
/// </summary>
public static class PayaffeWebhookVerifier
{
    /// <summary>
    /// The replay window the Webhook Event contract documents for receivers.
    /// </summary>
    public static readonly TimeSpan DefaultAllowedClockSkew = TimeSpan.FromMinutes(5);

    private const string _signatureScheme = "v1=";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Verifies a Delivery against the raw request bytes exactly as they were received.
    /// Re-serializing the body before verification changes the signature basis and is the
    /// most common reason a valid Delivery is rejected.
    /// </summary>
    public static PayaffeWebhookVerificationResult Verify(
        ReadOnlySpan<byte> requestBody,
        PayaffeWebhookHeaders headers,
        string secret,
        DateTimeOffset now,
        TimeSpan? allowedClockSkew = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        TimeSpan clockSkew = allowedClockSkew ?? DefaultAllowedClockSkew;
        if (clockSkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowedClockSkew),
                "The allowed clock skew must not be negative.");
        }

        Guid? deliveryId = Guid.TryParse(headers.DeliveryId, out Guid parsedDeliveryId)
            ? parsedDeliveryId
            : null;

        if (string.IsNullOrWhiteSpace(headers.Timestamp) ||
            string.IsNullOrWhiteSpace(headers.Signature))
        {
            return PayaffeWebhookVerificationResult.Rejected(
                PayaffeWebhookRejectionReason.MissingHeader,
                deliveryId);
        }

        if (!long.TryParse(
                headers.Timestamp,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out long unixTimestamp))
        {
            return PayaffeWebhookVerificationResult.Rejected(
                PayaffeWebhookRejectionReason.MalformedTimestamp,
                deliveryId);
        }

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return PayaffeWebhookVerificationResult.Rejected(
                PayaffeWebhookRejectionReason.MalformedTimestamp,
                deliveryId);
        }

        // A future timestamp is bounded by the same window, so a forged Delivery cannot buy
        // itself an unlimited lifetime by claiming to have been signed tomorrow.
        TimeSpan age = now - signedAt;
        if (age > clockSkew || age < -clockSkew)
        {
            return PayaffeWebhookVerificationResult.Rejected(
                PayaffeWebhookRejectionReason.TimestampOutsideWindow,
                deliveryId);
        }

        byte[] expectedSignature = ComputeSignature(secret, unixTimestamp, requestBody);
        switch (MatchSignature(headers.Signature, expectedSignature))
        {
            case SignatureMatch.Malformed:
                return PayaffeWebhookVerificationResult.Rejected(
                    PayaffeWebhookRejectionReason.MalformedSignature,
                    deliveryId);
            case SignatureMatch.Mismatch:
                return PayaffeWebhookVerificationResult.Rejected(
                    PayaffeWebhookRejectionReason.SignatureMismatch,
                    deliveryId);
        }

        PayaffeWebhookEvent? webhookEvent;
        try
        {
            webhookEvent = JsonSerializer.Deserialize<PayaffeWebhookEvent>(
                requestBody,
                _jsonOptions);
        }
        catch (JsonException)
        {
            return PayaffeWebhookVerificationResult.Rejected(
                PayaffeWebhookRejectionReason.MalformedPayload,
                deliveryId);
        }

        return webhookEvent is null || webhookEvent.Payment is null
            ? PayaffeWebhookVerificationResult.Rejected(
                PayaffeWebhookRejectionReason.MalformedPayload,
                deliveryId)
            : PayaffeWebhookVerificationResult.Verified(webhookEvent, deliveryId);
    }

    private static byte[] ComputeSignature(
        string secret,
        long unixTimestamp,
        ReadOnlySpan<byte> requestBody)
    {
        string prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"{unixTimestamp}.");
        int prefixLength = Encoding.UTF8.GetByteCount(prefix);
        byte[] signatureBase = new byte[prefixLength + requestBody.Length];
        Encoding.UTF8.GetBytes(prefix, signatureBase);
        requestBody.CopyTo(signatureBase.AsSpan(prefixLength));

        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signatureBase);
    }

    private static SignatureMatch MatchSignature(string headerValue, byte[] expectedSignature)
    {
        // Values are comma-separated so a future scheme or a secret rotation can present more
        // than one candidate. Every well-formed candidate is compared, because a receiver that
        // stopped at the first one would reject a Delivery that carried a valid signature.
        bool anyWellFormed = false;
        byte[] candidateSignature = new byte[HMACSHA256.HashSizeInBytes];
        foreach (Range range in headerValue.AsSpan().Split(','))
        {
            ReadOnlySpan<char> candidate = headerValue.AsSpan()[range].Trim();
            if (!candidate.StartsWith(_signatureScheme, StringComparison.Ordinal))
            {
                continue;
            }

            ReadOnlySpan<char> hex = candidate[_signatureScheme.Length..];
            if (hex.Length != candidateSignature.Length * 2 ||
                Convert.FromHexString(hex, candidateSignature, out _, out int written) != OperationStatus.Done ||
                written != candidateSignature.Length)
            {
                continue;
            }

            anyWellFormed = true;
            if (CryptographicOperations.FixedTimeEquals(expectedSignature, candidateSignature))
            {
                return SignatureMatch.Matched;
            }
        }

        return anyWellFormed ? SignatureMatch.Mismatch : SignatureMatch.Malformed;
    }

    private enum SignatureMatch
    {
        Matched,
        Mismatch,
        Malformed,
    }
}
