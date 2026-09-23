namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class MatchingBlockchainTransactionRecord
{
    public Guid ProjectId { get; set; } = ProjectDefaults.DefaultProjectId;

    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string PaymentAddress { get; set; } = string.Empty;

    public string TransactionHash { get; set; } = string.Empty;

    public string ObservedAmount { get; set; } = string.Empty;

    public DateTimeOffset ObservedAt { get; set; }

    public DateTimeOffset FirstObservedAt { get; set; }

    public int Confirmations { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string? ProviderObservationId { get; set; }

    public string? BlockHash { get; set; }

    public long? BlockHeight { get; set; }

    public DateTimeOffset LastCheckedAt { get; set; }

    public bool ContributedToCompletion { get; set; }

    public bool ReorgAffected { get; set; }

    public int ConsecutiveMissingCount { get; set; }

    public int ConsecutiveConfirmationDropCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
