namespace Payaffe.Application.Admin;

/// <summary>
/// A failure counter after one more failure was recorded against it.
/// </summary>
/// <param name="FailedAttemptCount">The count including this failure.</param>
/// <param name="LimitReached">True when this failure reached the configured limit.</param>
public sealed record AdminFailedAttemptOutcome(int FailedAttemptCount, bool LimitReached);
