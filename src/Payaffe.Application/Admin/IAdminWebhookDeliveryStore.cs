namespace Payaffe.Application.Admin;

public interface IAdminWebhookDeliveryStore
{
    Task<IReadOnlyList<AdminWebhookDeliveryReadModel>> ListResendableDeliveriesAsync(
        int limit,
        CancellationToken cancellationToken);
}
