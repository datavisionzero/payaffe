namespace Payaffe.Application.Payments;

public sealed record RecordBlockchainObservationCommand(
    Guid PaymentId,
    string SupportedCurrency,
    string PaymentAddress,
    string TransactionHash,
    string ObservedAmount,
    DateTimeOffset ObservedAt,
    int Confirmations,
    string ProviderName,
    string? ProviderObservationId);
