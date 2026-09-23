namespace Payaffe.Application.Admin;

/// <summary>
/// A transaction a Payment Address received before its Payment's currency
/// selection. It does not count toward the Payment (ADR 0035).
/// </summary>
public sealed record AdminAddressHistoryAlertReadModel(
    Guid ProjectId,
    Guid Id,
    Guid PaymentId,
    string SupportedCurrency,
    string PaymentAddress,
    string TransactionHash,
    string ObservedAmount,
    DateTimeOffset ObservedAt,
    DateTimeOffset CurrencySelectedAt,
    string Status,
    DateTimeOffset CreatedAt);
