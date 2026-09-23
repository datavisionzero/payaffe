namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class SimulatedTransactionRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string PaymentAddress { get; set; } = string.Empty;

    public string TransactionHash { get; set; } = string.Empty;

    public string Amount { get; set; } = string.Empty;

    public DateTimeOffset ObservedAt { get; set; }

    /// <summary>
    /// When the simulated observation first reported it. Until then it is
    /// unconfirmed; after that, the next poll reports it confirmed.
    /// </summary>
    public DateTimeOffset? FirstReportedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
