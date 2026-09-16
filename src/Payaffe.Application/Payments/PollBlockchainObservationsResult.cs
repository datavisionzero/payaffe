namespace Payaffe.Application.Payments;

public sealed record PollBlockchainObservationsResult(
    int TargetCount,
    int ObservationCount,
    int RecordedCount,
    int CompletedCount,
    int AlreadyRecordedCount,
    int RejectedCount,
    int FailedCount = 0);
