namespace Payaffe.Application.Admin;

public sealed record AdminRecoveryCodeReadModel(
    Guid Id,
    string CodeHash);
