namespace Payaffe.Application.Payments;

public sealed record SelectPaymentCurrencyCommand(
    string PayerPageId,
    string SupportedCurrency);

public sealed record SelectIntegrationPaymentCurrencyCommand(
    Guid IntegrationApiCredentialId,
    Guid PaymentId,
    string SupportedCurrency);
