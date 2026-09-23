using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfIntegrationApiCredentialAuthenticator(
    PayaffeDbContext dbContext,
    IClock clock,
    ILogger<EfIntegrationApiCredentialAuthenticator> logger)
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

        // LastUsedAt is bookkeeping. A write that loses to a concurrent change
        // of the same row, such as a rotation, must not turn a request that
        // authenticated into an error.
        credential.Credential.LastUsedAt = clock.UtcNow;
        credential.Credential.UpdatedAt = credential.Credential.LastUsedAt.Value;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(
                exception,
                "Recording the last use of Integration API Credential {CredentialId} failed.",
                credential.Credential.Id);
        }

        return new AuthenticatedIntegrationApiCredential(
            credential.Credential.Id,
            credential.Credential.ProjectId,
            credential.ProjectStatus,
            credential.Credential.Name);
    }
}
