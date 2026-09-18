namespace Payaffe.Application.Payments;

public sealed record CreatePaymentResult(
    CreatePaymentResultKind Kind,
    PaymentResponse? Payment,
    bool CreatedNew)
{
    public static CreatePaymentResult Created(PaymentResponse payment) =>
        new(CreatePaymentResultKind.Success, payment, CreatedNew: true);

    public static CreatePaymentResult Existing(PaymentResponse payment) =>
        new(CreatePaymentResultKind.Success, payment, CreatedNew: false);

    public static CreatePaymentResult IdempotencyConflict() =>
        new(CreatePaymentResultKind.IdempotencyConflict, Payment: null, CreatedNew: false);

    public static CreatePaymentResult ProjectUnavailable() =>
        new(CreatePaymentResultKind.ProjectUnavailable, Payment: null, CreatedNew: false);
}

public enum CreatePaymentResultKind
{
    Success,
    IdempotencyConflict,
    ProjectUnavailable,
}
