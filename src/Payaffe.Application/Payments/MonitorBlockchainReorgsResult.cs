namespace Payaffe.Application.Payments;

public sealed record MonitorBlockchainReorgsResult(
    int TargetCount,
    int CheckedCount,
    int UpdatedCount,
    int ReorgAlertCount,
    int MissingObservationCount,
    int FailedCount = 0);
