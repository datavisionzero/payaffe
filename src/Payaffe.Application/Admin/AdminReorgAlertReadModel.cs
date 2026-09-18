namespace Payaffe.Application.Admin;

public sealed record AdminReorgAlertReadModel(
    Guid ProjectId,
    Guid Id,
    Guid PaymentId,
    string SupportedCurrency,
    string TransactionHash,
    int PreviousConfirmations,
    int NewConfirmations,
    string? PreviousBlockHash,
    string? NewBlockHash,
    long? PreviousBlockHeight,
    long? NewBlockHeight,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);
