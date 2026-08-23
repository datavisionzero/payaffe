namespace Payaffe.Domain.Payments;

public sealed record PaymentStatus
{
    private PaymentStatus(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PaymentStatus PendingCurrencySelection { get; } = new("pending_currency_selection");

    public static PaymentStatus WaitingForPayment { get; } = new("waiting_for_payment");

    public static PaymentStatus Observed { get; } = new("observed");

    public static PaymentStatus Completed { get; } = new("completed");

    public static PaymentStatus Expired { get; } = new("expired");

    public static PaymentStatus Settled { get; } = new("settled");

    public static IReadOnlyCollection<PaymentStatus> All { get; } =
    [
        PendingCurrencySelection,
        WaitingForPayment,
        Observed,
        Completed,
        Expired,
        Settled,
    ];

    private static readonly IReadOnlyDictionary<string, PaymentStatus> Values =
        All.ToDictionary(status => status.Value, StringComparer.Ordinal);

    public static PaymentStatus FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException("Payment Status is required.", "payment_status.required");
        }

        var normalizedValue = value.Trim();
        if (Values.TryGetValue(normalizedValue, out var status))
        {
            return status;
        }

        throw new DomainRuleException("Payment Status is unknown.", "payment_status.unknown");
    }

    public override string ToString() => Value;
}
