using System.Security.Cryptography;
using System.Text;
using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NBitcoin;

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
    public async Task Populated_upgrade_and_restore_preserve_payment_states_history_addresses_webhooks_and_leases()
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
            values
                ('202609160001_AddProjectOwnership', '10.0.9'),
                ('202609160002_AddProjectPaymentConfigurationAndAddressSources', '10.0.9');
            """);
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            await dbContext.Database.MigrateAsync();
        }
        await ExecuteAsync(
            connectionString,
            "delete from \"__EFMigrationsHistory\" where \"MigrationId\" in (@migration_id, @address_migration_id)",
            ("migration_id", ProjectMigration),
            ("address_migration_id", "202609160002_AddProjectPaymentConfigurationAndAddressSources"));

        var credentialId = Guid.NewGuid();
        var activePaymentId = Guid.NewGuid();
        var expiredPaymentId = Guid.NewGuid();
        var completedPaymentId = Guid.NewGuid();
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
            values
                (@active_payment_id, @credential_id, 'legacy-active', 'EUR', 1500,
                 'waiting_for_payment', 'legacy-active-page', @now + interval '1 hour',
                 @now + interval '25 hours', @now, @now, 1),
                (@expired_payment_id, @credential_id, 'legacy-expired', 'EUR', 2500,
                 'expired', 'legacy-expired-page', @now - interval '2 hours',
                 @now - interval '1 hour', @now - interval '26 hours', @now, 2),
                (@completed_payment_id, @credential_id, 'legacy-completed', 'USD', 3500,
                 'completed', 'legacy-completed-page', @now - interval '3 hours',
                 @now + interval '21 hours', @now - interval '4 hours', @now, 3);

            insert into app.payment_event_history (id, payment_id, event_type, occurred_at, details)
            values (@payment_event_id, @active_payment_id, 'payment.created', @now, '{"legacy":true}'::jsonb);

            insert into app.payment_address_assignments (payment_id, supported_currency, payment_address, assigned_at)
            values (@active_payment_id, 'BTC', 'legacy-address', @now);

            insert into outbox.webhook_events
                (id, payment_id, integration_api_credential_id, event_type, event_version, payload_version,
                 resource_type, resource_id, status, occurred_at, created_at, next_attempt_at,
                 attempt_count, correlation_id)
            values (@webhook_event_id, @active_payment_id, @credential_id, 'payment.created', '1', 1,
                    'payment', @active_payment_id::text, 'pending', @now, @now, @now, 0, 'legacy-correlation');

            insert into app.background_worker_leases
                (worker_name, consecutive_failure_count, updated_at, version)
            values ('legacy-worker', 0, @now, 1);
            """,
            ("credential_id", credentialId),
            ("active_payment_id", activePaymentId),
            ("expired_payment_id", expiredPaymentId),
            ("completed_payment_id", completedPaymentId),
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
                (select worker_name from app.background_worker_leases where worker_name = 'legacy-worker'),
                (select count(*) from app.payments),
                (select string_agg(status, ',' order by status) from app.payments)
            """;
        command.Parameters.AddWithValue("payment_id", activePaymentId);
        command.Parameters.AddWithValue("payment_event_id", paymentEventId);
        command.Parameters.AddWithValue("webhook_event_id", webhookEventId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(ProjectDefaults.DefaultProjectId, reader.GetGuid(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal("legacy-address", reader.GetString(2));
        Assert.Equal("pending", reader.GetString(3));
        Assert.Equal("legacy-worker", reader.GetString(4));
        Assert.Equal(3, reader.GetInt64(5));
        Assert.Equal("completed,expired,waiting_for_payment", reader.GetString(6));
        await reader.DisposeAsync();
        await connection.DisposeAsync();

        NpgsqlConnection.ClearAllPools();
        var restoredConnectionString = await postgres.CloneDatabaseAsync(connectionString);
        await using var restoredProvider = BuildProvider(restoredConnectionString);
        await SchemaMigrator.ApplyAsync(restoredProvider, CancellationToken.None);
        await using var restoredScope = restoredProvider.CreateAsyncScope();
        var restoredDb = restoredScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Equal(
            new[]
            {
                (activePaymentId, "waiting_for_payment"),
                (expiredPaymentId, "expired"),
                (completedPaymentId, "completed"),
            }.OrderBy(payment => payment.Item1),
            await restoredDb.Payments
                .OrderBy(payment => payment.Id)
                .Select(payment => new ValueTuple<Guid, string>(payment.Id, payment.Status))
                .ToArrayAsync());
        Assert.All(restoredDb.Payments, payment => Assert.Equal(ProjectDefaults.DefaultProjectId, payment.ProjectId));
        Assert.Equal("legacy-address", Assert.Single(restoredDb.PaymentAddressAssignments).PaymentAddress);
        Assert.Equal("pending", Assert.Single(restoredDb.WebhookOutboxEvents).Status);
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

    [Fact]
    public async Task Credentials_derive_project_scope_for_creation_idempotency_and_reads()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        var otherProjectId = Guid.NewGuid();
        var disabledProjectId = Guid.NewGuid();
        var firstCredentialId = Guid.NewGuid();
        var peerCredentialId = Guid.NewGuid();
        var otherCredentialId = Guid.NewGuid();
        var disabledCredentialId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.Projects.AddRange(
            Project(otherProjectId, "other", "active", now),
            Project(disabledProjectId, "disabled", "disabled", now));
        dbContext.IntegrationApiCredentials.AddRange(
            Credential(ProjectDefaults.DefaultProjectId, firstCredentialId, "first-token", now),
            Credential(ProjectDefaults.DefaultProjectId, peerCredentialId, "peer-token", now),
            Credential(otherProjectId, otherCredentialId, "other-token", now),
            Credential(disabledProjectId, disabledCredentialId, "disabled-token", now));
        await dbContext.SaveChangesAsync();

        var store = scope.ServiceProvider.GetRequiredService<IPaymentStore>();
        var firstPaymentId = Guid.NewGuid();
        var otherPaymentId = Guid.NewGuid();
        var first = await store.CreateAsync(
            firstCredentialId,
            "same-key",
            "same-hash",
            Payment(firstPaymentId, firstCredentialId, "first-page", now),
            [],
            [],
            [],
            CancellationToken.None);
        var other = await store.CreateAsync(
            otherCredentialId,
            "same-key",
            "same-hash",
            Payment(otherPaymentId, otherCredentialId, "other-page", now),
            [],
            [],
            [],
            CancellationToken.None);
        var disabled = await store.CreateAsync(
            disabledCredentialId,
            "disabled-key",
            "disabled-hash",
            Payment(Guid.NewGuid(), disabledCredentialId, "disabled-page", now),
            [],
            [],
            [],
            CancellationToken.None);

        Assert.Equal(CreatePaymentStoreResultKind.Created, first.Kind);
        Assert.Equal(CreatePaymentStoreResultKind.Created, other.Kind);
        Assert.Equal(CreatePaymentStoreResultKind.ProjectUnavailable, disabled.Kind);
        Assert.NotNull(await store.FindAsync(peerCredentialId, firstPaymentId, CancellationToken.None));
        Assert.Null(await store.FindAsync(otherCredentialId, firstPaymentId, CancellationToken.None));
        Assert.Equal(
            [ProjectDefaults.DefaultProjectId, otherProjectId],
            await dbContext.Payments
                .OrderBy(payment => payment.PayerPageId)
                .Select(payment => payment.ProjectId)
                .ToArrayAsync());

        var authenticator = scope.ServiceProvider.GetRequiredService<IIntegrationApiCredentialAuthenticator>();
        var principal = await authenticator.AuthenticateAsync("other-token", CancellationToken.None);
        Assert.NotNull(principal);
        Assert.Equal(otherProjectId, principal.ProjectId);
        Assert.Equal("active", principal.ProjectStatus);
    }

    [Fact]
    public async Task Shared_watch_only_source_and_native_eth_pools_allocate_without_cross_project_reuse()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        var otherProjectId = Guid.NewGuid();
        var defaultPaymentId = Guid.NewGuid();
        var otherPaymentId = Guid.NewGuid();
        var defaultEthPaymentId = Guid.NewGuid();
        var otherEthPaymentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var extendedPublicKey = new ExtKey().Neuter().ToString(Network.TestNet);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"BTC\ntestnet\nsegwit\n{extendedPublicKey}")));

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            dbContext.Projects.Add(Project(otherProjectId, "address-other", "active", now));
            dbContext.ProjectWatchOnlyWalletSources.AddRange(
                WalletSource(ProjectDefaults.DefaultProjectId),
                WalletSource(otherProjectId));

            var defaultCredentialId = Guid.NewGuid();
            var otherCredentialId = Guid.NewGuid();
            dbContext.IntegrationApiCredentials.AddRange(
                Credential(ProjectDefaults.DefaultProjectId, defaultCredentialId, "address-default-token", now),
                Credential(otherProjectId, otherCredentialId, "address-other-token", now));
            dbContext.Payments.AddRange(
                PaymentRecord(ProjectDefaults.DefaultProjectId, defaultPaymentId, defaultCredentialId, "address-default", now),
                PaymentRecord(otherProjectId, otherPaymentId, otherCredentialId, "address-other", now),
                PaymentRecord(ProjectDefaults.DefaultProjectId, defaultEthPaymentId, defaultCredentialId, "eth-default", now),
                PaymentRecord(otherProjectId, otherEthPaymentId, otherCredentialId, "eth-other", now));

            var adminId = Guid.NewGuid();
            dbContext.AdminAccounts.Add(new AdminAccountRecord
            {
                Id = adminId,
                Username = "address-admin",
                NormalizedUsername = "ADDRESS-ADMIN",
                PasswordHash = "test-hash",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
            AddEthPool(ProjectDefaults.DefaultProjectId, "0x1111111111111111111111111111111111111111");
            AddEthPool(otherProjectId, "0x2222222222222222222222222222222222222222");
            await dbContext.SaveChangesAsync();

            ProjectWatchOnlyWalletSourceRecord WalletSource(Guid projectId) => new()
            {
                ProjectId = projectId,
                SupportedCurrency = "BTC",
                SourceFingerprint = fingerprint,
                Network = "testnet",
                AddressType = "segwit",
                StartingIndex = 0,
                SourceReference = $"xpub:{extendedPublicKey}",
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };

            void AddEthPool(Guid projectId, string address)
            {
                var importId = Guid.NewGuid();
                dbContext.NativeEthAddressPoolImports.Add(new NativeEthAddressPoolImportRecord
                {
                    ProjectId = projectId,
                    Id = importId,
                    ImportedByAdminAccountId = adminId,
                    AddressCount = 1,
                    ImportedAt = now,
                });
                dbContext.NativeEthAddresses.Add(new NativeEthAddressRecord
                {
                    ProjectId = projectId,
                    Id = Guid.NewGuid(),
                    ImportId = importId,
                    Address = address,
                    Status = "unused",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                });
            }
        }

        var defaultBtc = AssignAsync(ProjectDefaults.DefaultProjectId, defaultPaymentId, "BTC");
        var otherBtc = AssignAsync(otherProjectId, otherPaymentId, "BTC");
        var defaultEth = AssignAsync(ProjectDefaults.DefaultProjectId, defaultEthPaymentId, "ETH");
        var otherEth = AssignAsync(otherProjectId, otherEthPaymentId, "ETH");
        await Task.WhenAll(defaultBtc, otherBtc, defaultEth, otherEth);
        var defaultBtcAssignment = await defaultBtc;
        var otherBtcAssignment = await otherBtc;
        var defaultEthAssignment = await defaultEth;
        var otherEthAssignment = await otherEth;

        Assert.NotEqual(defaultBtcAssignment!.PaymentAddress, otherBtcAssignment!.PaymentAddress);
        Assert.Equal("0x1111111111111111111111111111111111111111", defaultEthAssignment!.PaymentAddress);
        Assert.Equal("0x2222222222222222222222222222222222222222", otherEthAssignment!.PaymentAddress);

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var cursor = Assert.Single(await verificationDb.WatchOnlyWalletCursors.ToListAsync());
        Assert.Equal(2, cursor.NextDerivationIndex);
        Assert.Equal(4, await verificationDb.PaymentAddressAssignments.CountAsync());

        async Task<PaymentAddressAssignment?> AssignAsync(Guid projectId, Guid paymentId, string currency)
        {
            await using var scope = provider.CreateAsyncScope();
            var addressProvider = scope.ServiceProvider.GetRequiredService<IPaymentAddressProvider>();
            return await addressProvider.AssignAsync(projectId, paymentId, currency, CancellationToken.None);
        }
    }

    private static ProjectRecord Project(
        Guid id,
        string slug,
        string status,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            Name = slug,
            Slug = slug,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static IntegrationApiCredentialRecord Credential(
        Guid projectId,
        Guid id,
        string token,
        DateTimeOffset now) =>
        new()
        {
            ProjectId = projectId,
            Id = id,
            Name = token,
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken(token),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static PaymentDraft Payment(
        Guid id,
        Guid credentialId,
        string payerPageId,
        DateTimeOffset now) =>
        new(
            id,
            credentialId,
            "shared-reference",
            "EUR",
            100,
            "pending_currency_selection",
            payerPageId,
            now.AddHours(1),
            now.AddHours(25),
            ContextUsername: null,
            ContextCustomerNumber: null,
            ContextCartName: null,
            ContextNote: null,
            ReturnUrl: null,
            now,
            now);

    private static PaymentRecord PaymentRecord(
        Guid projectId,
        Guid id,
        Guid credentialId,
        string payerPageId,
        DateTimeOffset now) => new()
    {
        ProjectId = projectId,
        Id = id,
        IntegrationApiCredentialId = credentialId,
        ExternalReference = payerPageId,
        FiatCurrency = "EUR",
        FiatAmountMinor = 100,
        Status = "pending_currency_selection",
        PayerPageId = payerPageId,
        ExpiresAt = now.AddHours(1),
        LateAcceptanceEndsAt = now.AddHours(25),
        CreatedAt = now,
        UpdatedAt = now,
    };

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
