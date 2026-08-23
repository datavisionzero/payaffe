using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class PayaffeDbContext(DbContextOptions<PayaffeDbContext> options) : DbContext(options)
{
    public DbSet<AdminAccountRecord> AdminAccounts => Set<AdminAccountRecord>();

    public DbSet<AdminLoginChallengeRecord> AdminLoginChallenges => Set<AdminLoginChallengeRecord>();

    public DbSet<AdminSessionRecord> AdminSessions => Set<AdminSessionRecord>();

    public DbSet<AdminRecoveryCodeRecord> AdminRecoveryCodes => Set<AdminRecoveryCodeRecord>();

    public DbSet<AuditLogEntryRecord> AuditLogEntries => Set<AuditLogEntryRecord>();

    public DbSet<IntegrationApiCredentialRecord> IntegrationApiCredentials => Set<IntegrationApiCredentialRecord>();

    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    public DbSet<PaymentOptionRecord> PaymentOptions => Set<PaymentOptionRecord>();

    public DbSet<PaymentCreationIdempotencyRecord> PaymentCreationIdempotency => Set<PaymentCreationIdempotencyRecord>();

    public DbSet<PaymentEventHistoryRecord> PaymentEventHistory => Set<PaymentEventHistoryRecord>();

    public DbSet<RateLockRecord> RateLocks => Set<RateLockRecord>();

    public DbSet<RateCacheRecord> RateCache => Set<RateCacheRecord>();

    public DbSet<PaymentAddressAssignmentRecord> PaymentAddressAssignments => Set<PaymentAddressAssignmentRecord>();

    public DbSet<WatchOnlyWalletCursorRecord> WatchOnlyWalletCursors => Set<WatchOnlyWalletCursorRecord>();

    public DbSet<NativeEthAddressPoolImportRecord> NativeEthAddressPoolImports => Set<NativeEthAddressPoolImportRecord>();

    public DbSet<NativeEthAddressRecord> NativeEthAddresses => Set<NativeEthAddressRecord>();

    public DbSet<ObservationHealthRecord> ObservationHealth => Set<ObservationHealthRecord>();

    public DbSet<BackgroundWorkerLeaseRecord> BackgroundWorkerLeases => Set<BackgroundWorkerLeaseRecord>();

    public DbSet<MatchingBlockchainTransactionRecord> MatchingBlockchainTransactions => Set<MatchingBlockchainTransactionRecord>();

    public DbSet<ReorgAlertRecord> ReorgAlerts => Set<ReorgAlertRecord>();

    public DbSet<WebhookOutboxEventRecord> WebhookOutboxEvents => Set<WebhookOutboxEventRecord>();

    public DbSet<WebhookEndpointRecord> WebhookEndpoints => Set<WebhookEndpointRecord>();

    public DbSet<WebhookDeliveryAttemptRecord> WebhookDeliveryAttempts => Set<WebhookDeliveryAttemptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAdminAccounts(modelBuilder);
        ConfigureAdminLoginChallenges(modelBuilder);
        ConfigureAdminSessions(modelBuilder);
        ConfigureAdminRecoveryCodes(modelBuilder);
        ConfigureAuditLogEntries(modelBuilder);
        ConfigureIntegrationApiCredentials(modelBuilder);
        ConfigurePayments(modelBuilder);
        ConfigurePaymentOptions(modelBuilder);
        ConfigurePaymentCreationIdempotency(modelBuilder);
        ConfigurePaymentEventHistory(modelBuilder);
        ConfigureRateLocks(modelBuilder);
        ConfigureRateCache(modelBuilder);
        ConfigurePaymentAddressAssignments(modelBuilder);
        ConfigureWatchOnlyWalletCursors(modelBuilder);
        ConfigureNativeEthAddressPool(modelBuilder);
        ConfigureObservationHealth(modelBuilder);
        ConfigureBackgroundWorkerLeases(modelBuilder);
        ConfigureMatchingBlockchainTransactions(modelBuilder);
        ConfigureReorgAlerts(modelBuilder);
        ConfigureWebhookEndpoints(modelBuilder);
        ConfigureWebhookOutboxEvents(modelBuilder);
        ConfigureWebhookDeliveryAttempts(modelBuilder);
    }

    private static void ConfigureAdminLoginChallenges(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminLoginChallengeRecord>(entity =>
        {
            entity.ToTable("admin_login_challenges", "auth");
            entity.HasKey(challenge => challenge.Id).HasName("pk_admin_login_challenges");

            entity.Property(challenge => challenge.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(challenge => challenge.AdminAccountId).HasColumnName("admin_account_id").HasColumnType("uuid");
            entity.Property(challenge => challenge.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
            entity.Property(challenge => challenge.FailedAttemptCount).HasColumnName("failed_attempt_count").HasColumnType("integer");
            entity.Property(challenge => challenge.ConsumedAt).HasColumnName("consumed_at").HasColumnType("timestamp with time zone");
            entity.Property(challenge => challenge.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(challenge => challenge.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasOne<AdminAccountRecord>()
                .WithMany()
                .HasForeignKey(challenge => challenge.AdminAccountId)
                .HasConstraintName("fk_admin_login_challenges_admin_accounts")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(challenge => new { challenge.AdminAccountId, challenge.ExpiresAt })
                .HasDatabaseName("ix_admin_login_challenges_account_expires_at");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_admin_login_challenges_failed_attempt_count",
                    "failed_attempt_count >= 0");
            });
        });
    }

    private static void ConfigureAdminSessions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminSessionRecord>(entity =>
        {
            entity.ToTable("admin_sessions", "auth");
            entity.HasKey(session => session.Id).HasName("pk_admin_sessions");

            entity.Property(session => session.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(session => session.AdminAccountId).HasColumnName("admin_account_id").HasColumnType("uuid");
            entity.Property(session => session.TokenHash).HasColumnName("token_hash").HasColumnType("text");
            entity.Property(session => session.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.IdleExpiresAt).HasColumnName("idle_expires_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.MfaAuthenticatedAt).HasColumnName("mfa_authenticated_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.StepUpAuthenticatedAt).HasColumnName("step_up_authenticated_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasOne<AdminAccountRecord>()
                .WithMany()
                .HasForeignKey(session => session.AdminAccountId)
                .HasConstraintName("fk_admin_sessions_admin_accounts")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(session => session.TokenHash)
                .IsUnique()
                .HasDatabaseName("uq_admin_sessions_token_hash");
            entity.HasIndex(session => new { session.AdminAccountId, session.ExpiresAt })
                .HasDatabaseName("ix_admin_sessions_account_expires_at");
        });
    }

    private static void ConfigureAdminAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminAccountRecord>(entity =>
        {
            entity.ToTable("admin_accounts", "auth");
            entity.HasKey(account => account.Id).HasName("pk_admin_accounts");

            entity.Property(account => account.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(account => account.Username).HasColumnName("username").HasColumnType("text");
            entity.Property(account => account.NormalizedUsername).HasColumnName("normalized_username").HasColumnType("text");
            entity.Property(account => account.PasswordHash).HasColumnName("password_hash").HasColumnType("text");
            entity.Property(account => account.TotpSecretReference).HasColumnName("totp_secret_reference").HasColumnType("text");
            entity.Property(account => account.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(account => account.FailedPasswordAttemptCount).HasColumnName("failed_password_attempt_count").HasColumnType("integer");
            entity.Property(account => account.LockedUntil).HasColumnName("locked_until").HasColumnType("timestamp with time zone");
            entity.Property(account => account.LastPasswordVerifiedAt).HasColumnName("last_password_verified_at").HasColumnType("timestamp with time zone");
            entity.Property(account => account.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(account => account.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(account => account.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasIndex(account => account.NormalizedUsername)
                .IsUnique()
                .HasDatabaseName("uq_admin_accounts_normalized_username");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_admin_accounts_status",
                    "status in ('active', 'disabled')");
                table.HasCheckConstraint(
                    "ck_admin_accounts_failed_password_attempt_count",
                    "failed_password_attempt_count >= 0");
            });
        });
    }

    private static void ConfigureAdminRecoveryCodes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminRecoveryCodeRecord>(entity =>
        {
            entity.ToTable("admin_recovery_codes", "auth");
            entity.HasKey(code => code.Id).HasName("pk_admin_recovery_codes");

            entity.Property(code => code.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(code => code.AdminAccountId).HasColumnName("admin_account_id").HasColumnType("uuid");
            entity.Property(code => code.CodeHash).HasColumnName("code_hash").HasColumnType("text");
            entity.Property(code => code.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(code => code.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(code => code.UsedAt).HasColumnName("used_at").HasColumnType("timestamp with time zone");
            entity.Property(code => code.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
            entity.Property(code => code.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasOne<AdminAccountRecord>()
                .WithMany()
                .HasForeignKey(code => code.AdminAccountId)
                .HasConstraintName("fk_admin_recovery_codes_admin_accounts")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(code => new { code.AdminAccountId, code.Status })
                .HasDatabaseName("ix_admin_recovery_codes_account_status");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_admin_recovery_codes_status",
                    "status in ('active', 'used', 'revoked')");
            });
        });
    }

    private static void ConfigureAuditLogEntries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntryRecord>(entity =>
        {
            entity.ToTable("audit_log_entries", "audit");
            entity.HasKey(auditEntry => auditEntry.EventId).HasName("pk_audit_log_entries");

            entity.Property(auditEntry => auditEntry.EventId).HasColumnName("event_id").HasColumnType("uuid");
            entity.Property(auditEntry => auditEntry.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
            entity.Property(auditEntry => auditEntry.EventType).HasColumnName("event_type").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.Outcome).HasColumnName("outcome").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.ActorType).HasColumnName("actor_type").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.ActorId).HasColumnName("actor_id").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.SourceService).HasColumnName("source_service").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.SourceIp).HasColumnName("source_ip").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.UserAgent).HasColumnName("user_agent").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.CorrelationId).HasColumnName("correlation_id").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.ReasonCode).HasColumnName("reason_code").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.SubjectType).HasColumnName("subject_type").HasColumnType("text");
            entity.Property(auditEntry => auditEntry.SubjectId).HasColumnName("subject_id").HasColumnType("text");

            entity.HasIndex(auditEntry => auditEntry.OccurredAt)
                .HasDatabaseName("ix_audit_log_entries_occurred_at");
            entity.HasIndex(auditEntry => new { auditEntry.EventType, auditEntry.OccurredAt })
                .HasDatabaseName("ix_audit_log_entries_event_type_occurred_at");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_audit_log_entries_outcome",
                    "outcome in ('success', 'failure', 'denied', 'expired', 'revoked')");
                table.HasCheckConstraint(
                    "ck_audit_log_entries_actor_type",
                    "actor_type in ('product_user', 'system')");
            });
        });
    }

    private static void ConfigureIntegrationApiCredentials(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationApiCredentialRecord>(entity =>
        {
            entity.ToTable("integration_api_credentials", "auth");
            entity.HasKey(credential => credential.Id).HasName("pk_integration_api_credentials");

            entity.Property(credential => credential.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(credential => credential.Name).HasColumnName("name").HasColumnType("text");
            entity.Property(credential => credential.TokenHash).HasColumnName("token_hash").HasColumnType("text");
            entity.Property(credential => credential.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(credential => credential.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(credential => credential.LastUsedAt).HasColumnName("last_used_at").HasColumnType("timestamp with time zone");
            entity.Property(credential => credential.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(credential => credential.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasIndex(credential => credential.TokenHash)
                .IsUnique()
                .HasDatabaseName("uq_integration_api_credentials_token_hash");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_integration_api_credentials_status",
                    "status in ('active', 'disabled')");
            });
        });
    }

    private static void ConfigurePayments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentRecord>(entity =>
        {
            entity.ToTable("payments", "app");
            entity.HasKey(payment => payment.Id).HasName("pk_payments");

            entity.Property(payment => payment.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(payment => payment.IntegrationApiCredentialId).HasColumnName("integration_api_credential_id").HasColumnType("uuid");
            entity.Property(payment => payment.ExternalReference).HasColumnName("external_reference").HasColumnType("text");
            entity.Property(payment => payment.FiatCurrency).HasColumnName("fiat_currency").HasColumnType("text");
            entity.Property(payment => payment.FiatAmountMinor).HasColumnName("fiat_amount_minor").HasColumnType("bigint");
            entity.Property(payment => payment.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(payment => payment.PayerPageId).HasColumnName("payer_page_id").HasColumnType("text");
            entity.Property(payment => payment.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
            entity.Property(payment => payment.LateAcceptanceEndsAt).HasColumnName("late_acceptance_ends_at").HasColumnType("timestamp with time zone");
            entity.Property(payment => payment.ContextUsername).HasColumnName("context_username").HasColumnType("text");
            entity.Property(payment => payment.ContextCustomerNumber).HasColumnName("context_customer_number").HasColumnType("text");
            entity.Property(payment => payment.ContextCartName).HasColumnName("context_cart_name").HasColumnType("text");
            entity.Property(payment => payment.ContextNote).HasColumnName("context_note").HasColumnType("text");
            entity.Property(payment => payment.ReturnUrl).HasColumnName("return_url").HasColumnType("text");
            entity.Property(payment => payment.SelectedCurrency).HasColumnName("selected_currency").HasColumnType("text");
            entity.Property(payment => payment.ExpectedCryptoAmount).HasColumnName("expected_crypto_amount").HasColumnType("text");
            entity.Property(payment => payment.PaymentAddress).HasColumnName("payment_address").HasColumnType("text");
            entity.Property(payment => payment.ConfirmedEligibleTotal).HasColumnName("confirmed_eligible_total").HasColumnType("text");
            entity.Property(payment => payment.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
            entity.Property(payment => payment.SettledAt).HasColumnName("settled_at").HasColumnType("timestamp with time zone");
            entity.Property(payment => payment.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(payment => payment.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(payment => payment.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasOne<IntegrationApiCredentialRecord>()
                .WithMany()
                .HasForeignKey(payment => payment.IntegrationApiCredentialId)
                .HasConstraintName("fk_payments_integration_api_credentials")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(payment => payment.PayerPageId)
                .IsUnique()
                .HasDatabaseName("uq_payments_payer_page_id");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_status",
                    "status in ('pending_currency_selection', 'waiting_for_payment', 'observed', 'completed', 'expired', 'settled')");
            });
        });
    }

    private static void ConfigurePaymentOptions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentOptionRecord>(entity =>
        {
            entity.ToTable("payment_options", "app");
            entity.HasKey(option => new { option.PaymentId, option.SupportedCurrency })
                .HasName("pk_payment_options");

            entity.Property(option => option.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(option => option.SupportedCurrency).HasColumnName("supported_currency").HasColumnType("text");
            entity.Property(option => option.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(option => option.UnavailableReason).HasColumnName("unavailable_reason").HasColumnType("text");
            entity.Property(option => option.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");

            entity.HasOne<PaymentRecord>()
                .WithMany()
                .HasForeignKey(option => option.PaymentId)
                .HasConstraintName("fk_payment_options_payments")
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_payment_options_status",
                    "status in ('available', 'unavailable')");
            });
        });
    }

    private static void ConfigurePaymentCreationIdempotency(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentCreationIdempotencyRecord>(entity =>
        {
            entity.ToTable("payment_creation_idempotency", "app");
            entity.HasKey(record => new { record.IntegrationApiCredentialId, record.IdempotencyKey })
                .HasName("pk_payment_creation_idempotency");

            entity.Property(record => record.IntegrationApiCredentialId).HasColumnName("integration_api_credential_id").HasColumnType("uuid");
            entity.Property(record => record.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("text");
            entity.Property(record => record.RequestHash).HasColumnName("request_hash").HasColumnType("text");
            entity.Property(record => record.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(record => record.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");

            entity.HasIndex(record => new { record.IntegrationApiCredentialId, record.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("uq_payment_creation_idempotency_credential_key");
            entity.HasOne<IntegrationApiCredentialRecord>()
                .WithMany()
                .HasForeignKey(record => record.IntegrationApiCredentialId)
                .HasConstraintName("fk_payment_creation_idempotency_integration_api_credentials")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentRecord>()
                .WithMany()
                .HasForeignKey(record => record.PaymentId)
                .HasConstraintName("fk_payment_creation_idempotency_payments")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePaymentEventHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentEventHistoryRecord>(entity =>
        {
            entity.ToTable("payment_event_history", "app");
            entity.HasKey(paymentEvent => paymentEvent.Id).HasName("pk_payment_event_history");

            entity.Property(paymentEvent => paymentEvent.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(paymentEvent => paymentEvent.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(paymentEvent => paymentEvent.EventType).HasColumnName("event_type").HasColumnType("text");
            entity.Property(paymentEvent => paymentEvent.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
            entity.Property(paymentEvent => paymentEvent.Details).HasColumnName("details").HasColumnType("jsonb");

            entity.HasOne<PaymentRecord>()
                .WithMany()
                .HasForeignKey(paymentEvent => paymentEvent.PaymentId)
                .HasConstraintName("fk_payment_event_history_payments")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRateLocks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RateLockRecord>(entity =>
        {
            entity.ToTable("rate_locks", "app");
            entity.HasKey(rateLock => rateLock.PaymentId).HasName("pk_rate_locks");

            entity.Property(rateLock => rateLock.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(rateLock => rateLock.SupportedCurrency).HasColumnName("supported_currency").HasColumnType("text");
            entity.Property(rateLock => rateLock.FiatCurrency).HasColumnName("fiat_currency").HasColumnType("text");
            entity.Property(rateLock => rateLock.FiatAmountMinor).HasColumnName("fiat_amount_minor").HasColumnType("bigint");
            entity.Property(rateLock => rateLock.ExpectedCryptoAmount).HasColumnName("expected_crypto_amount").HasColumnType("text");
            entity.Property(rateLock => rateLock.RateSource).HasColumnName("rate_source").HasColumnType("text");
            entity.Property(rateLock => rateLock.RateValue).HasColumnName("rate_value").HasColumnType("text");
            entity.Property(rateLock => rateLock.RateObservedAt).HasColumnName("rate_observed_at").HasColumnType("timestamp with time zone");
            entity.Property(rateLock => rateLock.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");

            entity.HasOne<PaymentRecord>()
                .WithOne()
                .HasForeignKey<RateLockRecord>(rateLock => rateLock.PaymentId)
                .HasConstraintName("fk_rate_locks_payments")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRateCache(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RateCacheRecord>(entity =>
        {
            entity.ToTable("rate_cache", "app");
            entity.HasKey(rate => new { rate.FiatCurrency, rate.SupportedCurrency })
                .HasName("pk_rate_cache");

            entity.Property(rate => rate.FiatCurrency)
                .HasColumnName("fiat_currency")
                .HasColumnType("text");
            entity.Property(rate => rate.SupportedCurrency)
                .HasColumnName("supported_currency")
                .HasColumnType("text");
            entity.Property(rate => rate.RateSource)
                .HasColumnName("rate_source")
                .HasColumnType("text");
            entity.Property(rate => rate.RateValue)
                .HasColumnName("rate_value")
                .HasColumnType("text");
            entity.Property(rate => rate.ObservedAt)
                .HasColumnName("observed_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(rate => rate.FetchedAt)
                .HasColumnName("fetched_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(rate => rate.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_rate_cache_fiat_currency",
                    "fiat_currency in ('EUR', 'USD')");
                table.HasCheckConstraint(
                    "ck_rate_cache_supported_currency",
                    "supported_currency in ('BTC', 'LTC', 'ETH')");
            });
        });
    }

    private static void ConfigurePaymentAddressAssignments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentAddressAssignmentRecord>(entity =>
        {
            entity.ToTable("payment_address_assignments", "app");
            entity.HasKey(assignment => assignment.PaymentId).HasName("pk_payment_address_assignments");

            entity.Property(assignment => assignment.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(assignment => assignment.SupportedCurrency).HasColumnName("supported_currency").HasColumnType("text");
            entity.Property(assignment => assignment.PaymentAddress).HasColumnName("payment_address").HasColumnType("text");
            entity.Property(assignment => assignment.AssignedAt).HasColumnName("assigned_at").HasColumnType("timestamp with time zone");

            entity.HasOne<PaymentRecord>()
                .WithOne()
                .HasForeignKey<PaymentAddressAssignmentRecord>(assignment => assignment.PaymentId)
                .HasConstraintName("fk_payment_address_assignments_payments")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureWatchOnlyWalletCursors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WatchOnlyWalletCursorRecord>(entity =>
        {
            entity.ToTable("watch_only_wallet_cursors", "app");
            entity.HasKey(cursor => cursor.SupportedCurrency)
                .HasName("pk_watch_only_wallet_cursors");
            entity.Property(cursor => cursor.SupportedCurrency)
                .HasColumnName("supported_currency")
                .HasColumnType("text");
            entity.Property(cursor => cursor.SourceFingerprint)
                .HasColumnName("source_fingerprint")
                .HasColumnType("text");
            entity.Property(cursor => cursor.NextDerivationIndex)
                .HasColumnName("next_derivation_index")
                .HasColumnType("bigint");
            entity.Property(cursor => cursor.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(cursor => cursor.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(cursor => cursor.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_watch_only_wallet_cursors_currency",
                    "supported_currency in ('BTC', 'LTC')");
                table.HasCheckConstraint(
                    "ck_watch_only_wallet_cursors_next_index",
                    "next_derivation_index >= 0");
            });
        });
    }

    private static void ConfigureNativeEthAddressPool(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NativeEthAddressPoolImportRecord>(entity =>
        {
            entity.ToTable("native_eth_address_pool_imports", "app");
            entity.HasKey(import => import.Id)
                .HasName("pk_native_eth_address_pool_imports");
            entity.Property(import => import.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(import => import.ImportedByAdminAccountId)
                .HasColumnName("imported_by_admin_account_id")
                .HasColumnType("uuid");
            entity.Property(import => import.AddressCount)
                .HasColumnName("address_count")
                .HasColumnType("integer");
            entity.Property(import => import.ImportedAt)
                .HasColumnName("imported_at")
                .HasColumnType("timestamp with time zone");
            entity.HasOne<AdminAccountRecord>()
                .WithMany()
                .HasForeignKey(import => import.ImportedByAdminAccountId)
                .HasConstraintName("fk_native_eth_address_pool_imports_admin_accounts")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NativeEthAddressRecord>(entity =>
        {
            entity.ToTable("native_eth_addresses", "app");
            entity.HasKey(address => address.Id).HasName("pk_native_eth_addresses");
            entity.Property(address => address.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(address => address.ImportId).HasColumnName("import_id").HasColumnType("uuid");
            entity.Property(address => address.Address).HasColumnName("address").HasColumnType("text");
            entity.Property(address => address.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(address => address.AssignedPaymentId)
                .HasColumnName("assigned_payment_id")
                .HasColumnType("uuid");
            entity.Property(address => address.AssignedAt)
                .HasColumnName("assigned_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(address => address.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(address => address.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(address => address.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();
            entity.HasOne<NativeEthAddressPoolImportRecord>()
                .WithMany()
                .HasForeignKey(address => address.ImportId)
                .HasConstraintName("fk_native_eth_addresses_imports")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentRecord>()
                .WithOne()
                .HasForeignKey<NativeEthAddressRecord>(address => address.AssignedPaymentId)
                .HasConstraintName("fk_native_eth_addresses_assigned_payments")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(address => address.Address)
                .IsUnique()
                .HasDatabaseName("uq_native_eth_addresses_address");
            entity.HasIndex(address => address.AssignedPaymentId)
                .IsUnique()
                .HasFilter("assigned_payment_id is not null")
                .HasDatabaseName("uq_native_eth_addresses_assigned_payment_id");
            entity.HasIndex(address => new { address.Status, address.CreatedAt })
                .HasDatabaseName("ix_native_eth_addresses_status_created_at");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_native_eth_addresses_status",
                    "status in ('unused', 'assigned', 'retired')");
            });
        });
    }

    private static void ConfigureObservationHealth(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ObservationHealthRecord>(entity =>
        {
            entity.ToTable("observation_health", "app");
            entity.HasKey(health => health.SupportedCurrency)
                .HasName("pk_observation_health");
            entity.Property(health => health.SupportedCurrency)
                .HasColumnName("supported_currency")
                .HasColumnType("text");
            entity.Property(health => health.ProviderName)
                .HasColumnName("provider_name")
                .HasColumnType("text");
            entity.Property(health => health.Status)
                .HasColumnName("status")
                .HasColumnType("text");
            entity.Property(health => health.LastSuccessfulAt)
                .HasColumnName("last_successful_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(health => health.LastFailedAt)
                .HasColumnName("last_failed_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(health => health.LastSafeErrorCode)
                .HasColumnName("last_safe_error_code")
                .HasColumnType("text");
            entity.Property(health => health.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(health => health.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_observation_health_currency",
                    "supported_currency in ('BTC', 'LTC', 'ETH')");
                table.HasCheckConstraint(
                    "ck_observation_health_status",
                    "status in ('available', 'unavailable')");
            });
        });
    }

    private static void ConfigureBackgroundWorkerLeases(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackgroundWorkerLeaseRecord>(entity =>
        {
            entity.ToTable("background_worker_leases", "app");
            entity.HasKey(lease => lease.WorkerName)
                .HasName("pk_background_worker_leases");
            entity.Property(lease => lease.WorkerName)
                .HasColumnName("worker_name")
                .HasColumnType("text");
            entity.Property(lease => lease.LockedBy)
                .HasColumnName("locked_by")
                .HasColumnType("text");
            entity.Property(lease => lease.LockedUntil)
                .HasColumnName("locked_until")
                .HasColumnType("timestamp with time zone");
            entity.Property(lease => lease.LastSucceededAt)
                .HasColumnName("last_succeeded_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(lease => lease.LastFailedAt)
                .HasColumnName("last_failed_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(lease => lease.LastSafeErrorCode)
                .HasColumnName("last_safe_error_code")
                .HasColumnType("text");
            entity.Property(lease => lease.ConsecutiveFailureCount)
                .HasColumnName("consecutive_failure_count")
                .HasColumnType("integer");
            entity.Property(lease => lease.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(lease => lease.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_background_worker_leases_failure_count",
                    "consecutive_failure_count >= 0");
            });
        });
    }

    private static void ConfigureMatchingBlockchainTransactions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchingBlockchainTransactionRecord>(entity =>
        {
            entity.ToTable("matching_blockchain_transactions", "app");
            entity.HasKey(transaction => transaction.Id).HasName("pk_matching_blockchain_transactions");

            entity.Property(transaction => transaction.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(transaction => transaction.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(transaction => transaction.SupportedCurrency).HasColumnName("supported_currency").HasColumnType("text");
            entity.Property(transaction => transaction.PaymentAddress).HasColumnName("payment_address").HasColumnType("text");
            entity.Property(transaction => transaction.TransactionHash).HasColumnName("transaction_hash").HasColumnType("text");
            entity.Property(transaction => transaction.ObservedAmount).HasColumnName("observed_amount").HasColumnType("text");
            entity.Property(transaction => transaction.ObservedAt).HasColumnName("observed_at").HasColumnType("timestamp with time zone");
            entity.Property(transaction => transaction.FirstObservedAt).HasColumnName("first_observed_at").HasColumnType("timestamp with time zone");
            entity.Property(transaction => transaction.Confirmations).HasColumnName("confirmations").HasColumnType("integer");
            entity.Property(transaction => transaction.ProviderName).HasColumnName("provider_name").HasColumnType("text");
            entity.Property(transaction => transaction.ProviderObservationId).HasColumnName("provider_observation_id").HasColumnType("text");
            entity.Property(transaction => transaction.BlockHash).HasColumnName("block_hash").HasColumnType("text");
            entity.Property(transaction => transaction.BlockHeight).HasColumnName("block_height").HasColumnType("bigint");
            entity.Property(transaction => transaction.LastCheckedAt).HasColumnName("last_checked_at").HasColumnType("timestamp with time zone");
            entity.Property(transaction => transaction.ContributedToCompletion).HasColumnName("contributed_to_completion").HasColumnType("boolean");
            entity.Property(transaction => transaction.ReorgAffected).HasColumnName("reorg_affected").HasColumnType("boolean");
            entity.Property(transaction => transaction.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(transaction => transaction.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(transaction => transaction.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasOne<PaymentRecord>()
                .WithMany()
                .HasForeignKey(transaction => transaction.PaymentId)
                .HasConstraintName("fk_matching_blockchain_transactions_payments")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(transaction => transaction.PaymentId)
                .HasDatabaseName("ix_matching_blockchain_transactions_payment_id");
            entity.HasIndex(transaction => new { transaction.SupportedCurrency, transaction.TransactionHash })
                .IsUnique()
                .HasDatabaseName("uq_matching_blockchain_transactions_currency_hash");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_matching_blockchain_transactions_confirmations",
                    "confirmations >= 0");
            });
        });
    }

    private static void ConfigureReorgAlerts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReorgAlertRecord>(entity =>
        {
            entity.ToTable("reorg_alerts", "app");
            entity.HasKey(alert => alert.Id).HasName("pk_reorg_alerts");

            entity.Property(alert => alert.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(alert => alert.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(alert => alert.MatchingBlockchainTransactionId).HasColumnName("matching_blockchain_transaction_id").HasColumnType("uuid");
            entity.Property(alert => alert.SupportedCurrency).HasColumnName("supported_currency").HasColumnType("text");
            entity.Property(alert => alert.TransactionHash).HasColumnName("transaction_hash").HasColumnType("text");
            entity.Property(alert => alert.PreviousConfirmations).HasColumnName("previous_confirmations").HasColumnType("integer");
            entity.Property(alert => alert.NewConfirmations).HasColumnName("new_confirmations").HasColumnType("integer");
            entity.Property(alert => alert.PreviousBlockHash).HasColumnName("previous_block_hash").HasColumnType("text");
            entity.Property(alert => alert.NewBlockHash).HasColumnName("new_block_hash").HasColumnType("text");
            entity.Property(alert => alert.PreviousBlockHeight).HasColumnName("previous_block_height").HasColumnType("bigint");
            entity.Property(alert => alert.NewBlockHeight).HasColumnName("new_block_height").HasColumnType("bigint");
            entity.Property(alert => alert.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(alert => alert.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(alert => alert.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(alert => alert.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasOne<PaymentRecord>()
                .WithMany()
                .HasForeignKey(alert => alert.PaymentId)
                .HasConstraintName("fk_reorg_alerts_payments")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MatchingBlockchainTransactionRecord>()
                .WithMany()
                .HasForeignKey(alert => alert.MatchingBlockchainTransactionId)
                .HasConstraintName("fk_reorg_alerts_matching_blockchain_transactions")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(alert => alert.PaymentId)
                .HasDatabaseName("ix_reorg_alerts_payment_id");
            entity.HasIndex(alert => alert.MatchingBlockchainTransactionId)
                .HasDatabaseName("ix_reorg_alerts_matching_blockchain_transaction_id");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_reorg_alerts_status",
                    "status in ('open', 'resolved')");
                table.HasCheckConstraint(
                    "ck_reorg_alerts_previous_confirmations",
                    "previous_confirmations >= 0");
                table.HasCheckConstraint(
                    "ck_reorg_alerts_new_confirmations",
                    "new_confirmations >= 0");
            });
        });
    }

    private static void ConfigureWebhookOutboxEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookOutboxEventRecord>(entity =>
        {
            entity.ToTable("webhook_events", "outbox");
            entity.HasKey(webhookEvent => webhookEvent.Id).HasName("pk_webhook_events");

            entity.Property(webhookEvent => webhookEvent.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(webhookEvent => webhookEvent.PaymentId).HasColumnName("payment_id").HasColumnType("uuid");
            entity.Property(webhookEvent => webhookEvent.IntegrationApiCredentialId).HasColumnName("integration_api_credential_id").HasColumnType("uuid");
            entity.Property(webhookEvent => webhookEvent.EventType).HasColumnName("event_type").HasColumnType("text");
            entity.Property(webhookEvent => webhookEvent.EventVersion).HasColumnName("event_version").HasColumnType("text");
            entity.Property(webhookEvent => webhookEvent.PayloadVersion).HasColumnName("payload_version").HasColumnType("integer");
            entity.Property(webhookEvent => webhookEvent.ResourceType).HasColumnName("resource_type").HasColumnType("text");
            entity.Property(webhookEvent => webhookEvent.ResourceId).HasColumnName("resource_id").HasColumnType("text");
            entity.Property(webhookEvent => webhookEvent.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(webhookEvent => webhookEvent.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
            entity.Property(webhookEvent => webhookEvent.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(webhookEvent => webhookEvent.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("timestamp with time zone");
            entity.Property(webhookEvent => webhookEvent.AttemptCount).HasColumnName("attempt_count").HasColumnType("integer");
            entity.Property(webhookEvent => webhookEvent.LockedBy).HasColumnName("locked_by").HasColumnType("text");
            entity.Property(webhookEvent => webhookEvent.LockedUntil).HasColumnName("locked_until").HasColumnType("timestamp with time zone");
            entity.Property(webhookEvent => webhookEvent.LastErrorCode).HasColumnName("last_error_code").HasColumnType("text");
            entity.Property(webhookEvent => webhookEvent.CorrelationId).HasColumnName("correlation_id").HasColumnType("text");

            entity.HasOne<PaymentRecord>()
                .WithMany()
                .HasForeignKey(webhookEvent => webhookEvent.PaymentId)
                .HasConstraintName("fk_webhook_events_payments")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<IntegrationApiCredentialRecord>()
                .WithMany()
                .HasForeignKey(webhookEvent => webhookEvent.IntegrationApiCredentialId)
                .HasConstraintName("fk_webhook_events_integration_api_credentials")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(webhookEvent => new { webhookEvent.Status, webhookEvent.NextAttemptAt })
                .HasDatabaseName("ix_webhook_events_status_next_attempt_at");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_webhook_events_status",
                    "status in ('pending', 'claimed', 'retry_pending', 'delivered', 'terminal_failed', 'cancelled')");
            });
        });
    }

    private static void ConfigureWebhookEndpoints(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookEndpointRecord>(entity =>
        {
            entity.ToTable("webhook_endpoints", "app");
            entity.HasKey(endpoint => endpoint.Id).HasName("pk_webhook_endpoints");

            entity.Property(endpoint => endpoint.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(endpoint => endpoint.IntegrationApiCredentialId).HasColumnName("integration_api_credential_id").HasColumnType("uuid");
            entity.Property(endpoint => endpoint.Url).HasColumnName("url").HasColumnType("text");
            entity.Property(endpoint => endpoint.SecretReference).HasColumnName("secret_reference").HasColumnType("text");
            entity.Property(endpoint => endpoint.Status).HasColumnName("status").HasColumnType("text");
            entity.Property(endpoint => endpoint.EventTypes).HasColumnName("event_types").HasColumnType("text");
            entity.Property(endpoint => endpoint.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(endpoint => endpoint.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(endpoint => endpoint.Version)
                .HasColumnName("version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();

            entity.HasOne<IntegrationApiCredentialRecord>()
                .WithMany()
                .HasForeignKey(endpoint => endpoint.IntegrationApiCredentialId)
                .HasConstraintName("fk_webhook_endpoints_integration_api_credentials")
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_webhook_endpoints_status",
                    "status in ('active', 'disabled')");
            });
        });
    }

    private static void ConfigureWebhookDeliveryAttempts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookDeliveryAttemptRecord>(entity =>
        {
            entity.ToTable("webhook_delivery_attempts", "outbox");
            entity.HasKey(attempt => attempt.Id).HasName("pk_webhook_delivery_attempts");

            entity.Property(attempt => attempt.Id).HasColumnName("id").HasColumnType("uuid");
            entity.Property(attempt => attempt.WebhookEventId).HasColumnName("webhook_event_id").HasColumnType("uuid");
            entity.Property(attempt => attempt.WebhookEndpointId).HasColumnName("webhook_endpoint_id").HasColumnType("uuid");
            entity.Property(attempt => attempt.AttemptNumber).HasColumnName("attempt_number").HasColumnType("integer");
            entity.Property(attempt => attempt.AttemptedAt).HasColumnName("attempted_at").HasColumnType("timestamp with time zone");
            entity.Property(attempt => attempt.Result).HasColumnName("result").HasColumnType("text");
            entity.Property(attempt => attempt.HttpStatusCode).HasColumnName("http_status_code").HasColumnType("integer");
            entity.Property(attempt => attempt.SafeErrorCode).HasColumnName("safe_error_code").HasColumnType("text");
            entity.Property(attempt => attempt.NextRetryAt).HasColumnName("next_retry_at").HasColumnType("timestamp with time zone");
            entity.Property(attempt => attempt.CorrelationId).HasColumnName("correlation_id").HasColumnType("text");

            entity.HasOne<WebhookOutboxEventRecord>()
                .WithMany()
                .HasForeignKey(attempt => attempt.WebhookEventId)
                .HasConstraintName("fk_webhook_delivery_attempts_webhook_events")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WebhookEndpointRecord>()
                .WithMany()
                .HasForeignKey(attempt => attempt.WebhookEndpointId)
                .HasConstraintName("fk_webhook_delivery_attempts_webhook_endpoints")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(attempt => attempt.WebhookEventId)
                .HasDatabaseName("ix_webhook_delivery_attempts_webhook_event_id");
            entity.HasIndex(attempt => attempt.WebhookEndpointId)
                .HasDatabaseName("ix_webhook_delivery_attempts_webhook_endpoint_id");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_webhook_delivery_attempts_result",
                    "result in ('succeeded', 'retry_pending', 'terminal_failed')");
            });
        });
    }
}
