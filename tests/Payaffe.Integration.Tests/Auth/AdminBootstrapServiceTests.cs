using Payaffe.Application;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Auth;

public sealed class AdminBootstrapServiceTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly DateTimeOffset TotpTestTime = DateTimeOffset.FromUnixTimeSeconds(59);
    private const string TotpSecretReference = "configuration:Admin:TotpSecrets:first-admin";

    [Fact]
    public async Task Creates_first_admin_recovery_codes_and_minimized_audit_once()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = await CreateServicesAsync(connectionString);

        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<AdminBootstrapService>()
            .BootstrapAsync(CreateRequest("correlation-1"), CancellationToken.None);

        Assert.Equal(AdminBootstrapStatus.Created, result.Status);
        Assert.NotNull(result.AdminAccountId);
        Assert.Equal(3, result.RecoveryCodes.Count);

        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var account = await dbContext.AdminAccounts.SingleAsync();
        Assert.Equal("admin@example.test", account.Username);
        Assert.Equal("ADMIN@EXAMPLE.TEST", account.NormalizedUsername);
        // No second factor at creation; enrolling one is the admin's own (ADR 0028).
        Assert.Null(account.TotpSecretReference);
        Assert.NotEqual("correct horse battery staple", account.PasswordHash);

        var passwordHasher = scope.ServiceProvider.GetRequiredService<IAdminPasswordHasher>();
        Assert.True(passwordHasher.VerifyPassword("correct horse battery staple", account.PasswordHash));
        var storedCodes = await dbContext.AdminRecoveryCodes.OrderBy(code => code.Id).ToArrayAsync();
        Assert.Equal(3, storedCodes.Length);
        Assert.All(storedCodes, code => Assert.DoesNotContain(result.RecoveryCodes, plain => plain == code.CodeHash));
        Assert.All(result.RecoveryCodes, plain =>
            Assert.Contains(storedCodes, stored => passwordHasher.VerifyPassword(plain, stored.CodeHash)));

        var audit = await dbContext.AuditLogEntries.SingleAsync(
            entry => entry.CorrelationId == "correlation-1");
        Assert.Equal("admin_account.bootstrap", audit.EventType);
        Assert.Equal("success", audit.Outcome);
        Assert.Equal("system", audit.ActorType);
        Assert.Equal("operations", audit.SourceService);
        Assert.Equal("correlation-1", audit.CorrelationId);
        Assert.Equal("admin_bootstrap.created", audit.ReasonCode);
        Assert.Equal(account.Id.ToString("D"), audit.SubjectId);
        Assert.DoesNotContain("correct horse battery staple", string.Join('|',
            audit.ActorId,
            audit.CorrelationId,
            audit.ReasonCode,
            audit.SubjectId));
    }

    [Fact]
    public async Task Refuses_bootstrap_after_first_admin_exists()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = await CreateServicesAsync(connectionString);

        using (var firstScope = services.CreateScope())
        {
            var first = await firstScope.ServiceProvider
                .GetRequiredService<AdminBootstrapService>()
                .BootstrapAsync(CreateRequest("correlation-first"), CancellationToken.None);
            Assert.Equal(AdminBootstrapStatus.Created, first.Status);
        }

        using var secondScope = services.CreateScope();
        var second = await secondScope.ServiceProvider
            .GetRequiredService<AdminBootstrapService>()
            .BootstrapAsync(CreateRequest("correlation-second"), CancellationToken.None);

        Assert.Equal(AdminBootstrapStatus.AlreadyBootstrapped, second.Status);
        var dbContext = secondScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Equal(1, await dbContext.AdminAccounts.CountAsync());
        var denial = await dbContext.AuditLogEntries.SingleAsync(
            entry => entry.CorrelationId == "correlation-second");
        Assert.Equal("denied", denial.Outcome);
        Assert.Equal("admin_bootstrap.already_completed", denial.ReasonCode);
    }

    [Fact]
    public async Task Concurrent_attempts_create_exactly_one_first_admin()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = await CreateServicesAsync(connectionString);

        async Task<AdminBootstrapResult> RunAsync(string correlationId)
        {
            using var scope = services.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<AdminBootstrapService>()
                .BootstrapAsync(CreateRequest(correlationId), CancellationToken.None);
        }

        var results = await Task.WhenAll(
            RunAsync("correlation-a"),
            RunAsync("correlation-b"));

        Assert.Single(results, result => result.Status == AdminBootstrapStatus.Created);
        Assert.Single(results, result => result.Status == AdminBootstrapStatus.AlreadyBootstrapped);

        using var verificationScope = services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Equal(1, await dbContext.AdminAccounts.CountAsync());
        Assert.Equal(3, await dbContext.AdminRecoveryCodes.CountAsync());
    }

    /// <summary>
    /// The first admin has no second factor, and that is the decision rather
    /// than an omission (ADR 0028). Enrolling one is the admin's own, later.
    /// </summary>
    [Fact]
    public async Task First_admin_is_created_without_a_second_factor()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = await CreateServicesAsync(connectionString);
        using var scope = services.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<AdminBootstrapService>()
            .BootstrapAsync(CreateRequest("correlation-no-mfa"), CancellationToken.None);

        Assert.Equal(AdminBootstrapStatus.Created, result.Status);

        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var account = await dbContext.AdminAccounts.SingleAsync();
        Assert.Null(account.TotpSecretReference);
    }

    [Theory]
    [InlineData("", "correct horse battery staple")]
    [InlineData("admin@example.test", "")]
    public async Task Incomplete_input_creates_nothing(string username, string password)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = await CreateServicesAsync(connectionString);
        using var scope = services.CreateScope();

        var request = CreateRequest("correlation-invalid") with
        {
            Username = username,
            Password = password,
        };
        var result = await scope.ServiceProvider
            .GetRequiredService<AdminBootstrapService>()
            .BootstrapAsync(request, CancellationToken.None);

        Assert.Equal(AdminBootstrapStatus.InvalidInput, result.Status);
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(await dbContext.AdminAccounts.ToArrayAsync());
        var audit = await dbContext.AuditLogEntries.SingleAsync(
            entry => entry.CorrelationId == "correlation-invalid");
        Assert.Equal("failure", audit.Outcome);
    }

    [Fact]
    public async Task A_password_shorter_than_sixteen_characters_creates_nothing()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = await CreateServicesAsync(connectionString);
        using var scope = services.CreateScope();

        var request = CreateRequest("correlation-short") with
        {
            Password = new string('x', AdminBootstrapService.MinimumPasswordLength - 1),
        };
        var result = await scope.ServiceProvider
            .GetRequiredService<AdminBootstrapService>()
            .BootstrapAsync(request, CancellationToken.None);

        Assert.Equal(AdminBootstrapStatus.InvalidInput, result.Status);
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Empty(await dbContext.AdminAccounts.ToArrayAsync());
        var audit = await dbContext.AuditLogEntries.SingleAsync(
            entry => entry.CorrelationId == "correlation-short");
        Assert.Equal("failure", audit.Outcome);
        Assert.Equal("admin_bootstrap.password_too_short", audit.ReasonCode);
    }

    private static AdminBootstrapRequest CreateRequest(string correlationId) =>
        new(
            "admin@example.test",
            "correct horse battery staple",
            correlationId);

    private static async Task<ServiceProvider> CreateServicesAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:TotpSecrets:first-admin"] = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
            })
            .Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IConfiguration>(configuration);
        serviceCollection.AddPayaffeApplication();
        serviceCollection.AddPayaffeInfrastructure(connectionString);
        serviceCollection.AddSingleton<IClock>(new FixedClock(TotpTestTime));
        serviceCollection.Configure<AdminAuthenticationOptions>(options =>
        {
            options.RecoveryCodeCount = 3;
            options.TotpAllowedTimeStepSkew = 0;
        });
        serviceCollection.AddScoped<AdminBootstrapService>();

        var services = serviceCollection.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(services, CancellationToken.None);
        return services;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
