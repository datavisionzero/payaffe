namespace Payaffe.Application.Payments;

public sealed record UpdateBlockchainTransactionConfirmationsCommand(
    Guid PaymentId,
    string SupportedCurrency,
    string TransactionHash,
    int Confirmations,
    string? BlockHash,
    long? BlockHeight,
    Guid ProjectId);
