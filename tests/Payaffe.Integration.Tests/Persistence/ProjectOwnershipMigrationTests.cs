using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Payaffe.Integration.Tests.Persistence;

public sealed class ProjectOwnershipMigrationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string ProjectMigration = "202609160001_AddProjectOwnership";

    [Fact]
    public async Task Fresh_install_creates_usable_default_project_from_effective_legacy_settings()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(
            connectionString,
            paymentExpiration: TimeSpan.FromMinutes(45),
            paymentTolerancePercent: 2.5m,
            ethLowCapacityThreshold: 9);

        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var project = Assert.Single(dbContext.Projects);
        Assert.Equal(ProjectDefaults.DefaultProjectId, project.Id);
        Assert.Equal(ProjectDefaults.DefaultProjectSlug, project.Slug);
        Assert.Equal("active", project.Status);

        var configuration = Assert.Single(dbContext.ProjectConfigurations);
        Assert.Equal(2700, configuration.PaymentExpirationSeconds);
        Assert.Equal(2.5m, configuration.PaymentTolerancePercent);
        Assert.Equal(9, configuration.NativeEthLowCapacityThreshold);
        Assert.Equal(64, configuration.LegacySettingsFingerprint.Length);
    }

    [Fact]
    public async Task Populated_upgrade_preserves_ids_history_addresses_webhooks_and_leases()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        await ExecuteAsync(
            connectionString,
            """
            create table "__EFMigrationsHistory" (
                "MigrationId" character varying(150) not null,
                "ProductVersion" character varying(32) not null,
                constraint "PK___EFMigrationsHistory" primary key ("MigrationId"));
            insert into "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            values ('202609160001_AddProjectOwnership', '10.0.9');
            """);
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            await dbContext.Database.MigrateAsync();
        }
        await ExecuteAsync(
            connectionString,
            "delete from \"__EFMigrationsHistory\" where \"MigrationId\" = @migration_id",
            ("migration_id", ProjectMigration));

        var credentialId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var paymentEventId = Guid.NewGuid();
        var webhookEventId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-09-16T12:00:00Z");
        await ExecuteAsync(
            connectionString,
            """
            insert into auth.integration_api_credentials
                (id, name, token_hash, status, created_at, updated_at, version)
            values (@credential_id, 'legacy', 'legacy-hash', 'active', @now, @now, 1);

            insert into app.payments
                (id, integration_api_credential_id, external_reference, fiat_currency, fiat_amount_minor,
                 status, payer_page_id, expires_at, late_acceptance_ends_at, created_at, updated_at, version)
            values (@payment_id, @credential_id, 'legacy-reference', 'EUR', 1500,
                    'waiting_for_payment', 'legacy-page', @now + interval '1 hour',
                    @now + interval '25 hours', @now, @now, 1);

            insert into app.payment_event_history (id, payment_id, event_type, occurred_at, details)
            values (@payment_event_id, @payment_id, 'payment.created', @now, '{"legacy":true}'::jsonb);

            insert into app.payment_address_assignments (payment_id, supported_currency, payment_address, assigned_at)
            values (@payment_id, 'BTC', 'legacy-address', @now);

            insert into outbox.webhook_events
                (id, payment_id, integration_api_credential_id, event_type, event_version, payload_version,
                 resource_type, resource_id, status, occurred_at, created_at, next_attempt_at,
                 attempt_count, correlation_id)
            values (@webhook_event_id, @payment_id, @credential_id, 'payment.created', '1', 1,
                    'payment', @payment_id::text, 'pending', @now, @now, @now, 0, 'legacy-correlation');

            insert into app.background_worker_leases
                (worker_name, consecutive_failure_count, updated_at, version)
            values ('legacy-worker', 0, @now, 1);
            """,
            ("credential_id", credentialId),
            ("payment_id", paymentId),
            ("payment_event_id", paymentEventId),
            ("webhook_event_id", webhookEventId),
            ("now", now));

        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                (select project_id from app.payments where id = @payment_id),
                (select count(*) from app.payment_event_history where id = @payment_event_id),
                (select payment_address from app.payment_address_assignments where payment_id = @payment_id),
                (select status from outbox.webhook_events where id = @webhook_event_id),
                (select worker_name from app.background_worker_leases where worker_name = 'legacy-worker')
            """;
        command.Parameters.AddWithValue("payment_id", paymentId);
        command.Parameters.AddWithValue("payment_event_id", paymentEventId);
        command.Parameters.AddWithValue("webhook_event_id", webhookEventId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(ProjectDefaults.DefaultProjectId, reader.GetGuid(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal("legacy-address", reader.GetString(2));
        Assert.Equal("pending", reader.GetString(3));
        Assert.Equal("legacy-worker", reader.GetString(4));
    }

    [Fact]
    public async Task Composite_relationship_rejects_cross_project_payment_child()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        var paymentId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await ExecuteAsync(
            connectionString,
            """
            insert into app.projects (id, name, slug, status, created_at, updated_at, version)
            values (@other_project_id, 'Other', 'other', 'active', @now, @now, 1);
            insert into auth.integration_api_credentials
                (project_id, id, name, token_hash, status, created_at, updated_at, version)
            values ('00000000-0000-0000-0000-000000000001', @credential_id, 'default', 'hash', 'active', @now, @now, 1);
            insert into app.payments
                (project_id, id, integration_api_credential_id, external_reference, fiat_currency,
                 fiat_amount_minor, status, payer_page_id, expires_at, late_acceptance_ends_at,
                 created_at, updated_at, version)
            values ('00000000-0000-0000-0000-000000000001', @payment_id, @credential_id, 'reference', 'EUR',
                    100, 'pending_currency_selection', 'page', @now, @now, @now, @now, 1);
            """,
            ("other_project_id", otherProjectId),
            ("credential_id", credentialId),
            ("payment_id", paymentId),
            ("now", now));

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            """
            insert into app.payment_event_history
                (project_id, id, payment_id, event_type, occurred_at)
            values (@other_project_id, @event_id, @payment_id, 'payment.created', @now)
            """,
            ("other_project_id", otherProjectId),
            ("event_id", Guid.NewGuid()),
            ("payment_id", paymentId),
            ("now", now)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task Restart_fails_when_hosts_disagree_on_legacy_settings()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = BuildProvider(connectionString, paymentTolerancePercent: 1m))
        {
            await SchemaMigrator.ApplyAsync(first, CancellationToken.None);
        }

        await using var second = BuildProvider(connectionString, paymentTolerancePercent: 3m);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SchemaMigrator.ApplyAsync(second, CancellationToken.None));

        Assert.Contains("differ", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        TimeSpan? paymentExpiration = null,
        decimal paymentTolerancePercent = 1m,
        int ethLowCapacityThreshold = 20)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString, registerHostedWorkers: false);
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PaymentExpiration = paymentExpiration ?? TimeSpan.FromHours(1);
            options.PaymentTolerancePercent = paymentTolerancePercent;
        });
        services.Configure<PaymentAddressOptions>(options =>
        {
            options.NativeEthLowCapacityThreshold = ethLowCapacityThreshold;
        });
        return services.BuildServiceProvider();
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
