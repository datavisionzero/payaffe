using Payaffe.Application;
using Payaffe.Application.Admin;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Auth;

/// <summary>
/// Parallel sign-in requests against a real database, where each request has
/// its own connection and the only thing between them is the version check.
/// </summary>
public sealed class AdminAuthenticationConcurrencyTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const int ParallelRequests = 8;
    private const string Password = "correct horse battery staple";
    private const string RecoveryCode = "ABCD-EFGH-JKLM";
    private const string SecondRecoveryCode = "NPQR-STUV-WXYZ";

    [Fact]
    public async Task A_Recovery_Code_redeemed_in_parallel_signs_in_exactly_once()
    {
        await using var services = await BuildServicesAsync();
        var adminAccountId = await SeedAccountAsync(services, RecoveryCode);
        var challengeIds = new List<Guid>();
        for (var index = 0; index < ParallelRequests; index++)
        {
            challengeIds.Add(await StartLoginAsync(services));
        }

        var results = await Task.WhenAll(challengeIds.Select(challengeId =>
            CompleteWithRecoveryCodeAsync(services, challengeId, RecoveryCode)));

        Assert.Single(results, result => result.Kind == AdminMfaCompleteResultKind.Authenticated);
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Single(await dbContext.AdminSessions.Where(session => session.AdminAccountId == adminAccountId).ToListAsync());
        Assert.Single(await dbContext.AdminRecoveryCodes.Where(code => code.Status == "used").ToListAsync());
    }

    [Fact]
    public async Task A_login_challenge_redeemed_in_parallel_signs_in_exactly_once()
    {
        await using var services = await BuildServicesAsync();
        await SeedAccountAsync(services, RecoveryCode, SecondRecoveryCode);
        var challengeId = await StartLoginAsync(services);

        // Two different valid codes against one challenge: the challenge is
        // what is single-use here.
        var results = await Task.WhenAll(
            CompleteWithRecoveryCodeAsync(services, challengeId, RecoveryCode),
            CompleteWithRecoveryCodeAsync(services, challengeId, SecondRecoveryCode));

        Assert.Single(results, result => result.Kind == AdminMfaCompleteResultKind.Authenticated);
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Single(await dbContext.AdminSessions.ToListAsync());
    }

    [Fact]
    public async Task Parallel_wrong_passwords_each_count_against_the_account()
    {
        await using var services = await BuildServicesAsync();
        var adminAccountId = await SeedAccountAsync(services, RecoveryCode);

        await Task.WhenAll(Enumerable.Range(0, ParallelRequests).Select(async _ =>
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<AdminAuthenticationService>()
                .StartLoginAsync(LoginCommand("wrong password"), CancellationToken.None);
            Assert.Equal(AdminLoginStartResultKind.InvalidCredentials, result.Kind);
        }));

        await using var verifyScope = services.CreateAsyncScope();
        var account = await verifyScope.ServiceProvider.GetRequiredService<PayaffeDbContext>()
            .AdminAccounts
            .SingleAsync(candidate => candidate.Id == adminAccountId);
        Assert.Equal(ParallelRequests, account.FailedPasswordAttemptCount);
    }

    [Fact]
    public async Task Parallel_wrong_codes_each_count_against_the_challenge()
    {
        await using var services = await BuildServicesAsync();
        await SeedAccountAsync(services, RecoveryCode);
        var challengeId = await StartLoginAsync(services);

        var results = await Task.WhenAll(Enumerable.Range(0, ParallelRequests).Select(_ =>
            CompleteWithRecoveryCodeAsync(services, challengeId, "ZZZZ-ZZZZ-ZZZZ")));

        Assert.All(results, result => Assert.Equal(AdminMfaCompleteResultKind.Invalid, result.Kind));
        await using var scope = services.CreateAsyncScope();
        var challenge = await scope.ServiceProvider.GetRequiredService<PayaffeDbContext>()
            .AdminLoginChallenges
            .SingleAsync(candidate => candidate.Id == challengeId);
        Assert.Equal(ParallelRequests, challenge.FailedAttemptCount);
    }

    private static async Task<Guid> StartLoginAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AdminAuthenticationService>()
            .StartLoginAsync(LoginCommand(Password), CancellationToken.None);
        Assert.Equal(AdminLoginStartResultKind.MfaRequired, result.Kind);
        return result.ChallengeId!.Value;
    }

    private static async Task<AdminMfaCompleteResult> CompleteWithRecoveryCodeAsync(
        ServiceProvider services,
        Guid challengeId,
        string recoveryCode)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AdminAuthenticationService>()
            .CompleteMfaAsync(
                new AdminMfaCompleteCommand(
                    challengeId,
                    TotpCode: null,
                    recoveryCode,
                    SourceIp: "203.0.113.10",
                    UserAgent: "integration-tests",
                    CorrelationId: Guid.NewGuid().ToString("D")),
                CancellationToken.None);
    }

    private static AdminLoginStartCommand LoginCommand(string password) =>
        new(
            "admin@example.test",
            password,
            SourceIp: "203.0.113.10",
            UserAgent: "integration-tests",
            CorrelationId: Guid.NewGuid().ToString("D"));

    private static async Task<Guid> SeedAccountAsync(ServiceProvider services, params string[] recoveryCodes)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var hasher = new AdminPasswordHasher();
        var now = DateTimeOffset.UtcNow;
        var adminAccountId = Guid.NewGuid();
        dbContext.AdminAccounts.Add(new AdminAccountRecord
        {
            Id = adminAccountId,
            Username = "admin@example.test",
            NormalizedUsername = "ADMIN@EXAMPLE.TEST",
            PasswordHash = hasher.HashPassword(Password),
            TotpSecretReference = "secret-ref:concurrency",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        });
        foreach (var recoveryCode in recoveryCodes)
        {
            dbContext.AdminRecoveryCodes.Add(new AdminRecoveryCodeRecord
            {
                Id = Guid.NewGuid(),
                AdminAccountId = adminAccountId,
                CodeHash = hasher.HashPassword(recoveryCode),
                Status = "active",
                CreatedAt = now,
            });
        }

        await dbContext.SaveChangesAsync();
        return adminAccountId;
    }

    private async Task<ServiceProvider> BuildServicesAsync()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        serviceCollection.AddLogging();
        serviceCollection.AddPayaffeApplication();
        serviceCollection.AddPayaffeInfrastructure(await postgres.CreateDatabaseAsync());

        // High enough that no request in a test is refused by a lock another
        // one set, so every request reaches the counter.
        serviceCollection.Configure<AdminAuthenticationOptions>(options =>
        {
            options.MaxFailedPasswordAttempts = 100;
            options.MaxFailedMfaAttempts = 100;
        });
        var services = serviceCollection.BuildServiceProvider();
        await SchemaMigrator.ApplyAsync(services, CancellationToken.None);
        return services;
    }
}
