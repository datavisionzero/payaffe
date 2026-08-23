namespace Payaffe.Application.Admin;

public sealed record AdminLoginStartCommand(
    string? Username,
    string? Password,
    string? SourceIp,
    string? UserAgent,
    string CorrelationId);
