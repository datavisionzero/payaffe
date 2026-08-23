namespace Payaffe.Application.Admin;

public sealed record AdminMfaCompleteCommand(
    Guid? ChallengeId,
    string? TotpCode,
    string? RecoveryCode,
    string? SourceIp,
    string? UserAgent,
    string CorrelationId);
