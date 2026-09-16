namespace Payaffe.Application.Admin;

public sealed class AdminWebhookDeliveryQueryService(IAdminWebhookDeliveryStore store)
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    public async Task<IReadOnlyList<AdminWebhookDeliveryReadModel>> ListResendableDeliveriesAsync(
        Guid projectId,
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(requestedLimit ?? DefaultLimit, 1, MaxLimit);
        return projectId == Guid.Empty
            ? []
            : await store.ListResendableDeliveriesAsync(projectId, limit, cancellationToken);
    }
}
