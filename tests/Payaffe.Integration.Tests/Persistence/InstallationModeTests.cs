using Payaffe.Application.Installation;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Payaffe.Integration.Tests.Persistence;

public sealed class InstallationModeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task An_unconfigured_installation_is_live()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(connectionString, mode: null);

        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        Assert.Equal("live", await ReadRecordedModeAsync(connectionString));
    }

    [Theory]
    [InlineData("live")]
    [InlineData("test")]
    [InlineData(" Test ")]
    public async Task The_first_start_records_the_configured_mode_and_a_matching_restart_is_accepted(string configured)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = BuildProvider(connectionString, configured))
        {
            await SchemaMigrator.ApplyAsync(first, CancellationToken.None);
        }

        await using var second = BuildProvider(connectionString, configured);
        await SchemaMigrator.ApplyAsync(second, CancellationToken.None);
        await using var scope = second.CreateAsyncScope();
        await InstallationModeRecord.VerifyAsync(scope.ServiceProvider, CancellationToken.None);

        Assert.Equal(configured.Trim().ToLowerInvariant(), await ReadRecordedModeAsync(connectionString));
    }

    [Theory]
    [InlineData("live", "test")]
    [InlineData("test", "live")]
    public async Task A_host_configured_for_the_other_mode_is_refused(string recorded, string configured)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = BuildProvider(connectionString, recorded))
        {
            await SchemaMigrator.ApplyAsync(first, CancellationToken.None);
        }

        await using var migrating = BuildProvider(connectionString, configured);
        var migrationRefusal = await Assert.ThrowsAsync<InstallationModeMismatchException>(
            () => SchemaMigrator.ApplyAsync(migrating, CancellationToken.None));
        await using var scope = migrating.CreateAsyncScope();
        var verificationRefusal = await Assert.ThrowsAsync<InstallationModeMismatchException>(
            () => InstallationModeRecord.VerifyAsync(scope.ServiceProvider, CancellationToken.None));

        Assert.Equal(recorded, ConfiguredInstallationMode.ToName(migrationRefusal.Recorded));
        Assert.Equal(configured, ConfiguredInstallationMode.ToName(verificationRefusal.Configured));
        Assert.Equal(recorded, await ReadRecordedModeAsync(connectionString));
    }

    /// <summary>
    /// Payments that exist before the mode did were observed on-chain, so the
    /// installation they belong to is live whatever the host now says.
    /// </summary>
    [Fact]
    public async Task A_database_with_payments_from_before_the_mode_existed_is_live()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = BuildProvider(connectionString, "live"))
        {
            await SchemaMigrator.ApplyAsync(first, CancellationToken.None);
            await using var scope = first.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var credentialId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
            {
                Id = credentialId,
                Name = "legacy",
                TokenHash = "legacy-hash",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
            dbContext.Payments.Add(new PaymentRecord
            {
                Id = Guid.NewGuid(),
                IntegrationApiCredentialId = credentialId,
                ExternalReference = "legacy-order",
                FiatCurrency = "EUR",
                FiatAmountMinor = 1500,
                Status = "pending_currency_selection",
                PayerPageId = "legacy-page",
                ExpiresAt = now.AddHours(1),
                LateAcceptanceEndsAt = now.AddHours(2),
                CreatedAt = now,
                UpdatedAt = now,
            });
            await dbContext.SaveChangesAsync();
        }

        await ExecuteAsync(
            connectionString,
            """
            drop table app.installation;
            delete from "__EFMigrationsHistory" where "MigrationId" = '202609230001_AddInstallationMode';
            """);

        await using var upgraded = BuildProvider(connectionString, "test");
        await Assert.ThrowsAsync<InstallationModeMismatchException>(
            () => SchemaMigrator.ApplyAsync(upgraded, CancellationToken.None));
        Assert.Equal("live", await ReadRecordedModeAsync(connectionString));
    }

    [Fact]
    public async Task A_host_that_does_not_migrate_accepts_a_database_nothing_has_recorded_yet()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(connectionString, "test");
        await using var scope = provider.CreateAsyncScope();

        await InstallationModeRecord.VerifyAsync(scope.ServiceProvider, CancellationToken.None);
    }

    [Fact]
    public void An_unknown_mode_is_a_configuration_error()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildProvider("Host=unused", "sandbox"));

        Assert.Contains("Installation:Mode", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(string connectionString, string? mode)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Installation:Mode"] = mode })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPayaffeInstallationMode(configuration);
        services.AddPayaffeInfrastructure(connectionString);
        return services.BuildServiceProvider();
    }

    private static async Task<string?> ReadRecordedModeAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select mode from app.installation where id = 1", connection);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
