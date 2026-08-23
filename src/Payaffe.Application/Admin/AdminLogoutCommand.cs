namespace Payaffe.Application.Admin;

public sealed record AdminLogoutCommand(
    string? SessionToken,
    string? SourceIp,
    string? UserAgent,
    string CorrelationId);
