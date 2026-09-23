using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Rate-limit partition keys for the Integration API and the Admin API.
/// </summary>
/// <remarks>
/// A partition is a route pattern, never a concrete path: keyed by path, every
/// Payment ID in <c>/api/v1/payments/{paymentId}</c> had a budget of its own.
///
/// An Integration API caller gets a partition of its own only once this host
/// has accepted its bearer token. Keyed by any presented token, every junk
/// <c>Authorization</c> value opened a fresh budget and a new partition, so a
/// flood of invented tokens was not limited at all. Tokens not yet accepted
/// share the route and source-address partition of callers without a token.
/// </remarks>
public sealed class RateLimitPartitions
{
    private readonly ConcurrentDictionary<string, byte> _acceptedCredentialFingerprints = new(StringComparer.Ordinal);

    public string GetIntegrationApiKey(HttpContext httpContext)
    {
        var route = GetRoute(httpContext);
        var sourceIp = GetSourceIp(httpContext);
        var fingerprint = GetCredentialFingerprint(httpContext);
        return fingerprint is not null && _acceptedCredentialFingerprints.ContainsKey(fingerprint)
            ? string.Join('|', route, fingerprint, sourceIp)
            : string.Join('|', route, "unauthenticated", sourceIp);
    }

    public static string GetAdminKey(HttpContext httpContext) =>
        string.Join('|', GetRoute(httpContext), GetSourceIp(httpContext));

    /// <summary>
    /// Records whether the request's bearer token authenticated, so an
    /// accepted token is limited on its own and a rejected one stops being.
    /// </summary>
    public void RecordAuthentication(HttpContext httpContext, bool accepted)
    {
        var fingerprint = GetCredentialFingerprint(httpContext);
        if (fingerprint is null)
        {
            return;
        }

        if (accepted)
        {
            _acceptedCredentialFingerprints.TryAdd(fingerprint, 0);
        }
        else
        {
            _acceptedCredentialFingerprints.TryRemove(fingerprint, out _);
        }
    }

    private static string GetRoute(HttpContext httpContext) =>
        (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
        ?? httpContext.Request.Path.Value
        ?? string.Empty;

    private static string GetSourceIp(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // The token itself never enters a key; its hash does.
    private static string? GetCredentialFingerprint(HttpContext httpContext)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        return string.IsNullOrWhiteSpace(authorization)
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authorization)));
    }
}

/// <summary>
/// Lets one rate-limit rejection per partition and window reach the Audit Log.
/// </summary>
/// <remarks>
/// Every rejected request used to write an Audit Log row, kept for 180 days, so
/// a flood of rejected requests became a flood of database writes. The first
/// rejection is the security-relevant fact; the rest of the window adds nothing.
/// </remarks>
public sealed class RateLimitRejectionAuditGate
{
    private const int PruneThreshold = 1024;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _auditedUntil = new(StringComparer.Ordinal);

    public bool ShouldAudit(string partitionKey, DateTimeOffset now, TimeSpan window)
    {
        if (_auditedUntil.Count > PruneThreshold)
        {
            foreach (var entry in _auditedUntil)
            {
                if (entry.Value <= now)
                {
                    _auditedUntil.TryRemove(entry);
                }
            }
        }

        var auditedUntil = now.Add(window);
        while (true)
        {
            if (_auditedUntil.TryAdd(partitionKey, auditedUntil))
            {
                return true;
            }

            if (!_auditedUntil.TryGetValue(partitionKey, out var current))
            {
                continue;
            }

            if (current > now)
            {
                return false;
            }

            if (_auditedUntil.TryUpdate(partitionKey, auditedUntil, current))
            {
                return true;
            }
        }
    }
}
