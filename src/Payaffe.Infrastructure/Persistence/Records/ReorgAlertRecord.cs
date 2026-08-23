namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class ReorgAlertRecord
{
    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public Guid MatchingBlockchainTransactionId { get; set; }

    public string SupportedCurrency { get; set; } = string.Empty;

    public string TransactionHash { get; set; } = string.Empty;

    public int PreviousConfirmations { get; set; }

    public int NewConfirmations { get; set; }

    public string? PreviousBlockHash { get; set; }

    public string? NewBlockHash { get; set; }

    public long? PreviousBlockHeight { get; set; }

    public long? NewBlockHeight { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; } = 1;
}
