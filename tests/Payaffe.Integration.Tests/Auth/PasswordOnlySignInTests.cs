using Payaffe.Application;
using Payaffe.Application.Admin;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Auth;

/// <summary>
/// Signing in without a second factor, against a real schema (ADR 0028).
/// </summary>
/// <remarks>
/// This exists because the API-level test for the same behaviour runs against
/// the in-memory provider, which has no NOT NULL constraints — so it passed
/// while `mfa_authenticated_at` was still required, and the first real sign-in
/// would have failed on the insert. A nullable column is only nullable where a
/// database says so.
/// </remarks>
public sealed class PasswordOnlySignInTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task An_account_created_by_bootstrap_signs_in_on_its_password()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var services = BuildServices(connectionString);
        await SchemaMigrator.ApplyAsync(services, CancellationToken.None);

        using (var bootstrapScope = services.CreateScope())
        {
            var bootstrap = await bootstrapScope.ServiceProvider
                .GetRequiredService<AdminBootstrapService>()
                .BootstrapAsync(
                    new AdminBootstrapRequest(
                        "admin@example.test",
                        "correct horse battery staple",
                        Guid.NewGuid().ToString("D")),
                    CancellationToken.None);
            Assert.Equal(AdminBootstrapStatus.Created, bootstrap.Status);
        }

        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<AdminAuthenticationService>()
            .StartLoginAsync(
                new AdminLoginStartCommand(
                    "admin@example.test",
                    "correct horse battery staple",
                    SourceIp: "203.0.113.10",
                    UserAgent: "integration-tests",
                    CorrelationId: Guid.NewGuid().ToString("D")),
                CancellationToken.None);

        // No second step, and a usable session token.
        Assert.Equal(AdminLoginStartResultKind.Authenticated, result.Kind);
        Assert.Null(result.ChallengeId);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionToken));

        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(await dbContext.AdminLoginChallenges.ToArrayAsync());

        // The row the in-memory provider could not have rejected.
        var session = await dbContext.AdminSessions.SingleAsync();
        Assert.Null(session.MfaAuthenticatedAt);
        Assert.Null(session.StepUpAuthenticatedAt);
    }

    private static ServiceProvider BuildServices(string connectionString)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        serviceCollection.AddLogging();
        serviceCollection.AddPayaffeApplication();
        serviceCollection.AddPayaffeInfrastructure(connectionString);
        serviceCollection.AddScoped<AdminBootstrapService>();
        return serviceCollection.BuildServiceProvider();
    }
}
