using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfIntegrationApiCredentialAuthenticator(
    PayaffeDbContext dbContext,
    IClock clock)
    : IIntegrationApiCredentialAuthenticator
{
    public async Task<AuthenticatedIntegrationApiCredential?> AuthenticateAsync(
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = IntegrationApiCredentialTokenHasher.HashToken(bearerToken);
        var credential = await dbContext.IntegrationApiCredentials
            .SingleOrDefaultAsync(
                candidate => candidate.TokenHash == tokenHash && candidate.Status == "active",
                cancellationToken);

        if (credential is null)
        {
            return null;
        }

        credential.LastUsedAt = clock.UtcNow;
        credential.UpdatedAt = credential.LastUsedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthenticatedIntegrationApiCredential(credential.Id, credential.Name);
    }
}
