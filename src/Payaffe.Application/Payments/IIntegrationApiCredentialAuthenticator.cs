namespace Payaffe.Application.Payments;

public interface IIntegrationApiCredentialAuthenticator
{
    Task<AuthenticatedIntegrationApiCredential?> AuthenticateAsync(
        string bearerToken,
        CancellationToken cancellationToken);
}
