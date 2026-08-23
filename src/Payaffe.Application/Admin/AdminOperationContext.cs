namespace Payaffe.Application.Admin;

/// <summary>
/// Identifies the Admin Account performing an operation and the surface it came
/// from. <paramref name="SourceService"/> distinguishes the Admin API from other
/// product surfaces such as the local Admin MCP host, so Audit Log entries stay
/// attributable to both the acting account and the surface it used.
/// </summary>
public sealed record AdminOperationContext(
    Guid AdminAccountId,
    string? SourceIp,
    string? UserAgent,
    string CorrelationId,
    string SourceService = "api");
