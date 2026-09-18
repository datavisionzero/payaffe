namespace Payaffe.Application.Payments;

public sealed record SelectPaymentCurrencyResult(
    SelectPaymentCurrencyResultKind Kind,
    PaymentResponse? Payment)
{
    public static SelectPaymentCurrencyResult Selected(PaymentResponse payment) =>
        new(SelectPaymentCurrencyResultKind.Selected, payment);

    public static SelectPaymentCurrencyResult AlreadySelected(PaymentResponse payment) =>
        new(SelectPaymentCurrencyResultKind.AlreadySelected, payment);

    public static SelectPaymentCurrencyResult CurrencyAlreadySelected(PaymentResponse payment) =>
        new(SelectPaymentCurrencyResultKind.CurrencyAlreadySelected, payment);

    public static SelectPaymentCurrencyResult NotFound() =>
        new(SelectPaymentCurrencyResultKind.NotFound, Payment: null);

    public static SelectPaymentCurrencyResult UnsupportedCurrency() =>
        new(SelectPaymentCurrencyResultKind.UnsupportedCurrency, Payment: null);

    public static SelectPaymentCurrencyResult PaymentExpired() =>
        new(SelectPaymentCurrencyResultKind.PaymentExpired, Payment: null);

    public static SelectPaymentCurrencyResult RateUnavailable() =>
        new(SelectPaymentCurrencyResultKind.RateUnavailable, Payment: null);

    public static SelectPaymentCurrencyResult PaymentAddressUnavailable() =>
        new(SelectPaymentCurrencyResultKind.PaymentAddressUnavailable, Payment: null);

    public static SelectPaymentCurrencyResult ObservationUnavailable() =>
        new(SelectPaymentCurrencyResultKind.ObservationUnavailable, Payment: null);

    public static SelectPaymentCurrencyResult CurrencyDisabled() =>
        new(SelectPaymentCurrencyResultKind.CurrencyDisabled, Payment: null);

    public static SelectPaymentCurrencyResult ProjectArchived() =>
        new(SelectPaymentCurrencyResultKind.ProjectArchived, Payment: null);
}

public enum SelectPaymentCurrencyResultKind
{
    Selected,
    AlreadySelected,
    CurrencyAlreadySelected,
    NotFound,
    UnsupportedCurrency,
    PaymentExpired,
    RateUnavailable,
    PaymentAddressUnavailable,
    ObservationUnavailable,
    CurrencyDisabled,
    ProjectArchived,
}
