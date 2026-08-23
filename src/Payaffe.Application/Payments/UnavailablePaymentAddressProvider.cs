namespace Payaffe.Application.Payments;

public sealed class UnavailablePaymentAddressProvider : IPaymentAddressProvider
{
    public Task<PaymentAddressAssignment?> AssignAsync(
        Guid paymentId,
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<PaymentAddressAssignment?>(null);
    }

    public Task<bool> IsAddressAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
