namespace Payaffe.Application.Webhooks;

public interface IWebhookSecretResolver
{
    Task<string?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken);
}
