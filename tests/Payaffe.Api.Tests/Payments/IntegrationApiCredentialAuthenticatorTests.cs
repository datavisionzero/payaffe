using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;

namespace Payaffe.Api.Tests.Payments;

public sealed class IntegrationApiCredentialAuthenticatorTests
{
    private const string Token = "authenticator-test-token";

    /// <summary>
    /// A rotation that changes the credential row between the read and the
    /// LastUsedAt write makes that write lose its concurrency check. The
    /// request had already authenticated, and must stay authenticated.
    /// </summary>
    [Fact]
    public async Task A_failed_last_used_write_does_not_fail_authentication()
    {
        var databaseName = $"authenticator-{Guid.NewGuid()}";
        var credentialId = await SeedAsync(databaseName);
        await using var dbContext = new PayaffeDbContext(new DbContextOptionsBuilder<PayaffeDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new ConflictingSaveInterceptor())
            .Options);
        var authenticator = new EfIntegrationApiCredentialAuthenticator(
            dbContext,
            new FixedClock(),
            NullLogger<EfIntegrationApiCredentialAuthenticator>.Instance);

        var principal = await authenticator.AuthenticateAsync(Token, CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal(credentialId, principal.Id);
    }

    private static async Task<Guid> SeedAsync(string databaseName)
    {
        await using var dbContext = new PayaffeDbContext(new DbContextOptionsBuilder<PayaffeDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        dbContext.Projects.Add(new ProjectRecord
        {
            Id = projectId,
            Name = "Test Project",
            Slug = projectId.ToString("N"),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        });
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            ProjectId = projectId,
            Id = credentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken(Token),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
        return credentialId;
    }

    private sealed class ConflictingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateConcurrencyException("The credential row changed.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }
}
