namespace Payaffe.Application.Admin;

public sealed record AdminStepUpCommand(
    string? SessionToken,
    string? TotpCode,
    string? SourceIp,
    string? UserAgent,
    string CorrelationId);
