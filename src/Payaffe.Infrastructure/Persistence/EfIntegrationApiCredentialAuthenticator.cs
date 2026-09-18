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
        var credential = await (
                from candidate in dbContext.IntegrationApiCredentials
                join project in dbContext.Projects on candidate.ProjectId equals project.Id
                where candidate.TokenHash == tokenHash && candidate.Status == "active"
                select new
                {
                    Credential = candidate,
                    ProjectStatus = project.Status,
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (credential is null)
        {
            return null;
        }

        credential.Credential.LastUsedAt = clock.UtcNow;
        credential.Credential.UpdatedAt = credential.Credential.LastUsedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthenticatedIntegrationApiCredential(
            credential.Credential.Id,
            credential.Credential.ProjectId,
            credential.ProjectStatus,
            credential.Credential.Name);
    }
}
