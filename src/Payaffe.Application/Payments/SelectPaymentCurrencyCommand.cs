namespace Payaffe.Application.Payments;

public sealed record SelectPaymentCurrencyCommand(
    string PayerPageId,
    string SupportedCurrency);
