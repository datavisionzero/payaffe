namespace Payaffe.Application.Webhooks;

public interface IWebhookSecretResolver
{
    /// <summary>
    /// Resolves a Webhook Endpoint secret reference for the Project that owns
    /// the endpoint. A reference into another Project's namespace resolves to
    /// nothing.
    /// </summary>
    Task<string?> ResolveForProjectAsync(
        Guid projectId,
        string secretReference,
        CancellationToken cancellationToken);
}
