namespace Payaffe.Application.Webhooks;

public sealed class UnavailableWebhookSecretResolver : IWebhookSecretResolver
{
    public Task<string?> ResolveForProjectAsync(
        Guid projectId,
        string secretReference,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}
