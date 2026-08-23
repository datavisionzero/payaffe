namespace Payaffe.Infrastructure.Persistence.Records;

public sealed class PaymentCreationIdempotencyRecord
{
    public Guid IntegrationApiCredentialId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public Guid PaymentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
