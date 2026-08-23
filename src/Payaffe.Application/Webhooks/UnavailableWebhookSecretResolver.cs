namespace Payaffe.Application.Webhooks;

public sealed class UnavailableWebhookSecretResolver : IWebhookSecretResolver
{
    public Task<string?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}
