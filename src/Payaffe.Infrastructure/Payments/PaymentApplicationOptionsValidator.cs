using Payaffe.Application.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class PaymentApplicationOptionsValidator : IValidateOptions<PaymentApplicationOptions>
{
    /// <summary>
    /// Zero expires an Observed Payment when its Late Acceptance Window ends,
    /// as before the wait existed. The upper bound keeps a transaction that
    /// will never confirm from holding a Payment open indefinitely.
    /// </summary>
    public static readonly TimeSpan MaxObservedConfirmationWait = TimeSpan.FromDays(30);

    public ValidateOptionsResult Validate(string? name, PaymentApplicationOptions options) =>
        options.ObservedConfirmationWait < TimeSpan.Zero ||
        options.ObservedConfirmationWait > MaxObservedConfirmationWait
            ? ValidateOptionsResult.Fail("Payments:ObservedConfirmationWait must be between zero and 30 days.")
            : ValidateOptionsResult.Success;
}
