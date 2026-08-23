namespace Payaffe.Application.Payments;

public sealed record UpdateBlockchainTransactionConfirmationsResult(
    UpdateBlockchainTransactionConfirmationsResultKind Kind,
    PaymentResponse? Payment)
{
    public static UpdateBlockchainTransactionConfirmationsResult Updated(PaymentResponse payment) =>
        new(UpdateBlockchainTransactionConfirmationsResultKind.Updated, payment);

    public static UpdateBlockchainTransactionConfirmationsResult Completed(PaymentResponse payment) =>
        new(UpdateBlockchainTransactionConfirmationsResultKind.Completed, payment);

    public static UpdateBlockchainTransactionConfirmationsResult ReorgAlerted(PaymentResponse payment) =>
        new(UpdateBlockchainTransactionConfirmationsResultKind.ReorgAlerted, payment);

    public static UpdateBlockchainTransactionConfirmationsResult PaymentNotFound() =>
        new(UpdateBlockchainTransactionConfirmationsResultKind.PaymentNotFound, Payment: null);

    public static UpdateBlockchainTransactionConfirmationsResult PaymentNotReady() =>
        new(UpdateBlockchainTransactionConfirmationsResultKind.PaymentNotReady, Payment: null);

    public static UpdateBlockchainTransactionConfirmationsResult TransactionNotFound() =>
        new(UpdateBlockchainTransactionConfirmationsResultKind.TransactionNotFound, Payment: null);
}

public enum UpdateBlockchainTransactionConfirmationsResultKind
{
    Updated,
    Completed,
    ReorgAlerted,
    PaymentNotFound,
    PaymentNotReady,
    TransactionNotFound,
}
