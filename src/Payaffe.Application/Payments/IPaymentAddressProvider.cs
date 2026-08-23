namespace Payaffe.Application.Payments;

public interface IPaymentAddressProvider
{
    Task<PaymentAddressAssignment?> AssignAsync(
        Guid paymentId,
        string supportedCurrency,
        CancellationToken cancellationToken);

    Task<bool> IsAddressAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

public sealed record PaymentAddressAssignment(
    string SupportedCurrency,
    string PaymentAddress);
