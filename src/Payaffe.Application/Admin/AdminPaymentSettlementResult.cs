namespace Payaffe.Application.Admin;

public sealed record AdminPaymentSettlementResult(
    AdminPaymentSettlementResultKind Kind,
    AdminPaymentDetailReadModel? Payment)
{
    public static AdminPaymentSettlementResult Settled(AdminPaymentDetailReadModel payment) =>
        new(AdminPaymentSettlementResultKind.Settled, payment);

    public static AdminPaymentSettlementResult NotFound() =>
        new(AdminPaymentSettlementResultKind.NotFound, null);

    public static AdminPaymentSettlementResult NotSettleable(AdminPaymentDetailReadModel payment) =>
        new(AdminPaymentSettlementResultKind.NotSettleable, payment);

    public static AdminPaymentSettlementResult ConcurrencyConflict(AdminPaymentDetailReadModel payment) =>
        new(AdminPaymentSettlementResultKind.ConcurrencyConflict, payment);

    public static AdminPaymentSettlementResult InvalidInput() =>
        new(AdminPaymentSettlementResultKind.InvalidInput, null);
}

public enum AdminPaymentSettlementResultKind
{
    Settled,
    NotFound,
    NotSettleable,
    ConcurrencyConflict,
    InvalidInput,
}
