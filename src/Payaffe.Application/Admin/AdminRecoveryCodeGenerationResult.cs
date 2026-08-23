namespace Payaffe.Application.Admin;

public sealed record AdminRecoveryCodeGenerationResult(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> RecoveryCodes);
