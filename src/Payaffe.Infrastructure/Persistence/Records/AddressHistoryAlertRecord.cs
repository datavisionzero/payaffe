namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class AddressHistoryAlertRecord
{
    public Guid ProjectId { get; set; }

    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string PaymentAddress { get; set; } = string.Empty;

    public string TransactionHash { get; set; } = string.Empty;

    public string ObservedAmount { get; set; } = string.Empty;

    public DateTimeOffset ObservedAt { get; set; }

    public DateTimeOffset CurrencySelectedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
