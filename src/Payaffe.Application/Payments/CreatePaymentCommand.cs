namespace Payaffe.Application.Payments;

public sealed record CreatePaymentCommand(
    string? FiatCurrency,
    long FiatAmountMinor,
    string? ExternalReference,
    PaymentContextCommand? PaymentContext,
    string? ReturnUrl,
    string? IdempotencyKey);

public sealed record PaymentContextCommand(
    string? Username,
    string? CustomerNumber,
    string? CartName,
    string? Note);
