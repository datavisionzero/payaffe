namespace Payaffe.Application.Webhooks;

public interface IWebhookSecretResolver
{
    Task<string?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken);

    Task<string?> ResolveForProjectAsync(
        Guid projectId,
        string secretReference,
        CancellationToken cancellationToken) => ResolveAsync(secretReference, cancellationToken);
}
