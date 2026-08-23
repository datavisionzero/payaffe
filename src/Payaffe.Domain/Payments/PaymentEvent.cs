namespace Payaffe.Domain.Payments;

public abstract record PaymentEvent(Guid Id, Guid PaymentId, string EventType, DateTimeOffset OccurredAt);

public sealed record PaymentCreatedEvent(Guid Id, Guid PaymentId, DateTimeOffset OccurredAt)
    : PaymentEvent(Id, PaymentId, "payment.created", OccurredAt);
