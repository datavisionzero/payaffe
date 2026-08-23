namespace Payaffe.Application.Payments;

public sealed record RecordBlockchainObservationResult(
    RecordBlockchainObservationResultKind Kind,
    PaymentResponse? Payment)
{
    public static RecordBlockchainObservationResult Observed(PaymentResponse payment) =>
        new(RecordBlockchainObservationResultKind.Observed, payment);

    public static RecordBlockchainObservationResult Completed(PaymentResponse payment) =>
        new(RecordBlockchainObservationResultKind.Completed, payment);

    public static RecordBlockchainObservationResult AlreadyRecorded(PaymentResponse payment) =>
        new(RecordBlockchainObservationResultKind.AlreadyRecorded, payment);

    public static RecordBlockchainObservationResult PaymentNotFound() =>
        new(RecordBlockchainObservationResultKind.PaymentNotFound, Payment: null);

    public static RecordBlockchainObservationResult PaymentNotReady() =>
        new(RecordBlockchainObservationResultKind.PaymentNotReady, Payment: null);

    public static RecordBlockchainObservationResult ObservationMismatch() =>
        new(RecordBlockchainObservationResultKind.ObservationMismatch, Payment: null);
}

public enum RecordBlockchainObservationResultKind
{
    Observed,
    Completed,
    AlreadyRecorded,
    PaymentNotFound,
    PaymentNotReady,
    ObservationMismatch,
}
