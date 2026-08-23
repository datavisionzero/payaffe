namespace Payaffe.Application.Admin;

public sealed record AdminRecoveryCodeDraft(
    Guid Id,
    Guid AdminAccountId,
    string CodeHash,
    DateTimeOffset CreatedAt);
