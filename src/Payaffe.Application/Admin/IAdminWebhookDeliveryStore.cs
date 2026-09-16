namespace Payaffe.Application.Admin;

public interface IAdminWebhookDeliveryStore
{
    Task<IReadOnlyList<AdminWebhookDeliveryReadModel>> ListResendableDeliveriesAsync(
        Guid projectId,
        int limit,
        CancellationToken cancellationToken);
}
