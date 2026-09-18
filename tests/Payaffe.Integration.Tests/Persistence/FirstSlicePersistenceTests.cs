using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Payaffe.Integration.Tests.Persistence;

public sealed class FirstSlicePersistenceTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Migration_runner_applies_first_slice_schema()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);

        var tables = await QueryListAsync(
            connectionString,
            """
            select table_schema || '.' || table_name
            from information_schema.tables
            where table_schema in ('app', 'audit', 'auth', 'outbox')
            order by table_schema, table_name
            """);

        Assert.Equal(
            [
                "app.background_worker_leases",
                "app.matching_blockchain_transactions",
                "app.native_eth_address_pool_imports",
                "app.native_eth_addresses",
                "app.observation_health",
                "app.payment_address_assignments",
                "app.payment_creation_idempotency",
                "app.payment_event_history",
                "app.payment_options",
                "app.payments",
                "app.project_configuration",
                "app.project_watch_only_wallet_sources",
                "app.projects",
                "app.rate_cache",
                "app.rate_locks",
                "app.reorg_alerts",
                "app.watch_only_wallet_cursors",
                "app.webhook_endpoints",
                "audit.audit_log_entries",
                "auth.admin_accounts",
                "auth.admin_login_challenges",
                "auth.admin_recovery_codes",
                "auth.admin_sessions",
                "auth.integration_api_credentials",
                "outbox.webhook_delivery_attempts",
                "outbox.webhook_events",
            ],
            tables);

        var paymentColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'app' and table_name = 'payments'
            """);

        Assert.Equal("uuid", paymentColumns["id"]);
        Assert.Equal("uuid", paymentColumns["project_id"]);
        Assert.Equal("uuid", paymentColumns["integration_api_credential_id"]);
        Assert.Equal("bigint", paymentColumns["fiat_amount_minor"]);
        Assert.Equal("timestamp with time zone", paymentColumns["expires_at"]);
        Assert.Equal("timestamp with time zone", paymentColumns["late_acceptance_ends_at"]);
        Assert.Equal("text", paymentColumns["selected_currency"]);
        Assert.Equal("text", paymentColumns["expected_crypto_amount"]);
        Assert.Equal("text", paymentColumns["payment_address"]);
        Assert.Equal("integer", paymentColumns["confirmation_requirement"]);
        Assert.Equal("numeric", paymentColumns["payment_tolerance_percent"]);
        Assert.Equal("integer", paymentColumns["reorg_monitoring_depth"]);
        Assert.Equal("text", paymentColumns["confirmed_eligible_total"]);
        Assert.Equal("timestamp with time zone", paymentColumns["completed_at"]);
        Assert.Equal("timestamp with time zone", paymentColumns["settled_at"]);
        Assert.Equal("bigint", paymentColumns["version"]);

        var paymentAddressAssignmentColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'app' and table_name = 'payment_address_assignments'
            """);

        Assert.Equal("text", paymentAddressAssignmentColumns["network"]);
        Assert.Equal("bigint", paymentAddressAssignmentColumns["chain_id"]);

        var eventColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'app' and table_name = 'payment_event_history'
            """);

        Assert.Equal("uuid", eventColumns["id"]);
        Assert.Equal("timestamp with time zone", eventColumns["occurred_at"]);
        Assert.Equal("jsonb", eventColumns["details"]);

        var webhookEventColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'outbox' and table_name = 'webhook_events'
            """);

        Assert.Equal("uuid", webhookEventColumns["id"]);
        Assert.Equal("uuid", webhookEventColumns["payment_id"]);
        Assert.Equal("uuid", webhookEventColumns["integration_api_credential_id"]);
        Assert.Equal("timestamp with time zone", webhookEventColumns["occurred_at"]);
        Assert.Equal("timestamp with time zone", webhookEventColumns["next_attempt_at"]);
        Assert.Equal("timestamp with time zone", webhookEventColumns["locked_until"]);

        var webhookEndpointColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'app' and table_name = 'webhook_endpoints'
            """);

        Assert.Equal("uuid", webhookEndpointColumns["id"]);
        Assert.Equal("uuid", webhookEndpointColumns["integration_api_credential_id"]);
        Assert.Equal("text", webhookEndpointColumns["secret_reference"]);
        Assert.Equal("timestamp with time zone", webhookEndpointColumns["created_at"]);

        var rateCacheColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'app' and table_name = 'rate_cache'
            """);

        Assert.Equal("text", rateCacheColumns["fiat_currency"]);
        Assert.Equal("text", rateCacheColumns["supported_currency"]);
        Assert.Equal("text", rateCacheColumns["rate_source"]);
        Assert.Equal("text", rateCacheColumns["rate_value"]);
        Assert.Equal("timestamp with time zone", rateCacheColumns["observed_at"]);
        Assert.Equal("timestamp with time zone", rateCacheColumns["fetched_at"]);
        Assert.Equal("bigint", rateCacheColumns["version"]);

        var deliveryAttemptColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'outbox' and table_name = 'webhook_delivery_attempts'
            """);

        Assert.Equal("uuid", deliveryAttemptColumns["id"]);
        Assert.Equal("uuid", deliveryAttemptColumns["webhook_event_id"]);
        Assert.Equal("uuid", deliveryAttemptColumns["webhook_endpoint_id"]);
        Assert.Equal("integer", deliveryAttemptColumns["attempt_number"]);
        Assert.Equal("timestamp with time zone", deliveryAttemptColumns["attempted_at"]);
        Assert.Equal("timestamp with time zone", deliveryAttemptColumns["next_retry_at"]);

        var matchingTransactionColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'app' and table_name = 'matching_blockchain_transactions'
            """);

        Assert.Equal("uuid", matchingTransactionColumns["id"]);
        Assert.Equal("uuid", matchingTransactionColumns["payment_id"]);
        Assert.Equal("text", matchingTransactionColumns["supported_currency"]);
        Assert.Equal("text", matchingTransactionColumns["transaction_hash"]);
        Assert.Equal("text", matchingTransactionColumns["observed_amount"]);
        Assert.Equal("timestamp with time zone", matchingTransactionColumns["observed_at"]);
        Assert.Equal("timestamp with time zone", matchingTransactionColumns["first_observed_at"]);
        Assert.Equal("integer", matchingTransactionColumns["confirmations"]);
        Assert.Equal("boolean", matchingTransactionColumns["reorg_affected"]);

        var reorgAlertColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'app' and table_name = 'reorg_alerts'
            """);

        Assert.Equal("uuid", reorgAlertColumns["id"]);
        Assert.Equal("uuid", reorgAlertColumns["payment_id"]);
        Assert.Equal("uuid", reorgAlertColumns["matching_blockchain_transaction_id"]);
        Assert.Equal("integer", reorgAlertColumns["previous_confirmations"]);
        Assert.Equal("integer", reorgAlertColumns["new_confirmations"]);
        Assert.Equal("text", reorgAlertColumns["previous_block_hash"]);
        Assert.Equal("text", reorgAlertColumns["new_block_hash"]);
        Assert.Equal("bigint", reorgAlertColumns["previous_block_height"]);
        Assert.Equal("bigint", reorgAlertColumns["new_block_height"]);
        Assert.Equal("text", reorgAlertColumns["status"]);
        Assert.Equal("text", matchingTransactionColumns["block_hash"]);
        Assert.Equal("bigint", matchingTransactionColumns["block_height"]);
        Assert.Equal("timestamp with time zone", matchingTransactionColumns["last_checked_at"]);
        Assert.Equal("boolean", matchingTransactionColumns["contributed_to_completion"]);

        var adminAccountColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'auth' and table_name = 'admin_accounts'
            """);

        Assert.Equal("uuid", adminAccountColumns["id"]);
        Assert.Equal("text", adminAccountColumns["normalized_username"]);
        Assert.Equal("text", adminAccountColumns["password_hash"]);
        Assert.Equal("text", adminAccountColumns["totp_secret_reference"]);
        Assert.Equal("integer", adminAccountColumns["failed_password_attempt_count"]);
        Assert.Equal("timestamp with time zone", adminAccountColumns["locked_until"]);
        Assert.Equal("timestamp with time zone", adminAccountColumns["last_password_verified_at"]);
        Assert.Equal("bigint", adminAccountColumns["version"]);

        var adminLoginChallengeColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'auth' and table_name = 'admin_login_challenges'
            """);

        Assert.Equal("uuid", adminLoginChallengeColumns["id"]);
        Assert.Equal("uuid", adminLoginChallengeColumns["admin_account_id"]);
        Assert.Equal("timestamp with time zone", adminLoginChallengeColumns["expires_at"]);
        Assert.Equal("integer", adminLoginChallengeColumns["failed_attempt_count"]);
        Assert.Equal("timestamp with time zone", adminLoginChallengeColumns["consumed_at"]);
        Assert.Equal("bigint", adminLoginChallengeColumns["version"]);

        var adminSessionColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'auth' and table_name = 'admin_sessions'
            """);

        Assert.Equal("uuid", adminSessionColumns["id"]);
        Assert.Equal("uuid", adminSessionColumns["admin_account_id"]);
        Assert.Equal("text", adminSessionColumns["token_hash"]);
        Assert.Equal("timestamp with time zone", adminSessionColumns["expires_at"]);
        Assert.Equal("timestamp with time zone", adminSessionColumns["idle_expires_at"]);
        Assert.Equal("timestamp with time zone", adminSessionColumns["mfa_authenticated_at"]);
        Assert.Equal("timestamp with time zone", adminSessionColumns["step_up_authenticated_at"]);
        Assert.Equal("timestamp with time zone", adminSessionColumns["revoked_at"]);
        Assert.Equal("bigint", adminSessionColumns["version"]);

        var adminRecoveryCodeColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'auth' and table_name = 'admin_recovery_codes'
            """);

        Assert.Equal("uuid", adminRecoveryCodeColumns["id"]);
        Assert.Equal("uuid", adminRecoveryCodeColumns["admin_account_id"]);
        Assert.Equal("text", adminRecoveryCodeColumns["code_hash"]);
        Assert.Equal("text", adminRecoveryCodeColumns["status"]);
        Assert.Equal("timestamp with time zone", adminRecoveryCodeColumns["created_at"]);
        Assert.Equal("timestamp with time zone", adminRecoveryCodeColumns["used_at"]);
        Assert.Equal("timestamp with time zone", adminRecoveryCodeColumns["revoked_at"]);
        Assert.Equal("bigint", adminRecoveryCodeColumns["version"]);

        var auditLogColumns = await QueryDictionaryAsync(
            connectionString,
            """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'audit' and table_name = 'audit_log_entries'
            """);

        Assert.Equal("uuid", auditLogColumns["event_id"]);
        Assert.Equal("uuid", auditLogColumns["project_id"]);
        Assert.Equal("timestamp with time zone", auditLogColumns["occurred_at"]);
        Assert.Equal("text", auditLogColumns["event_type"]);
        Assert.Equal("text", auditLogColumns["outcome"]);
        Assert.Equal("text", auditLogColumns["actor_type"]);
        Assert.Equal("text", auditLogColumns["correlation_id"]);
        Assert.Equal("text", auditLogColumns["reason_code"]);
    }

    [Fact]
    public async Task Migration_uses_lower_snake_case_names_and_idempotency_unique_index()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);

        var names = await QueryListAsync(
            connectionString,
            """
            select table_name from information_schema.tables where table_schema in ('app', 'audit', 'auth', 'outbox')
            union all
            select column_name from information_schema.columns where table_schema in ('app', 'audit', 'auth', 'outbox')
            """);

        Assert.All(names, name =>
        {
            Assert.Equal(name.ToLowerInvariant(), name);
            Assert.DoesNotContain("-", name, StringComparison.Ordinal);
        });

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select indexdef
            from pg_indexes
            where schemaname = 'app'
              and tablename = 'payment_creation_idempotency'
              and indexname = 'uq_payment_creation_idempotency_project_credential_key'
            """;

        var indexDefinition = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Contains("UNIQUE", indexDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project_id", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("integration_api_credential_id", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("idempotency_key", indexDefinition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Payment_creation_persists_first_slice_rows()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();

        var result = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                new PaymentContextCommand("customer@example.test", "C-1000", "Starter", "Note"),
                "https://example.test/orders/order-123",
                "create-order-123"),
            CancellationToken.None);

        Assert.Equal(CreatePaymentResultKind.Success, result.Kind);
        Assert.True(result.CreatedNew);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(dbContext.Payments);
        Assert.Equal(result.Payment!.PaymentId, payment.Id);
        Assert.Equal("pending_currency_selection", payment.Status);
        Assert.Equal("EUR", payment.FiatCurrency);
        Assert.Equal(1999, payment.FiatAmountMinor);
        Assert.Equal("order-123", payment.ExternalReference);

        Assert.Equal(3, dbContext.PaymentOptions.Count());
        Assert.All(dbContext.PaymentOptions, option => Assert.Equal("available", option.Status));
        var paymentEvent = Assert.Single(dbContext.PaymentEventHistory);
        Assert.Equal("payment.created", paymentEvent.EventType);
        var webhookEvent = Assert.Single(dbContext.WebhookOutboxEvents);
        Assert.Equal("payment.created", webhookEvent.EventType);
        Assert.Equal("1", webhookEvent.EventVersion);
        Assert.Equal(1, webhookEvent.PayloadVersion);
        Assert.Equal("payment", webhookEvent.ResourceType);
        Assert.Equal(payment.Id.ToString("D"), webhookEvent.ResourceId);
        Assert.Equal("pending", webhookEvent.Status);
        Assert.Equal(0, webhookEvent.AttemptCount);
        var idempotency = Assert.Single(dbContext.PaymentCreationIdempotency);
        Assert.Equal("create-order-123", idempotency.IdempotencyKey);
        Assert.Equal(payment.Id, idempotency.PaymentId);
    }

    [Fact]
    public async Task Payment_currency_selection_persists_rate_lock_address_assignment_and_event()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);

        var selectResult = await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);

        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selectResult.Kind);
        Assert.Equal(createResult.Payment!.PaymentId, selectResult.Payment!.PaymentId);
        Assert.Equal("waiting_for_payment", selectResult.Payment.Status);
        Assert.Equal("BTC", selectResult.Payment.SelectedCurrency);
        Assert.Equal("0.00039980", selectResult.Payment.ExpectedCryptoAmount);
        Assert.Equal("bc1qpayaffetestaddress0000000000000000000000000", selectResult.Payment.PaymentAddress);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(dbContext.Payments);
        Assert.Equal("waiting_for_payment", payment.Status);
        Assert.Equal("BTC", payment.SelectedCurrency);
        Assert.Equal("0.00039980", payment.ExpectedCryptoAmount);
        Assert.Equal("bc1qpayaffetestaddress0000000000000000000000000", payment.PaymentAddress);
        Assert.Equal(1, payment.ConfirmationRequirement);
        Assert.Equal(1m, payment.PaymentTolerancePercent);
        Assert.Equal(6, payment.ReorgMonitoringDepth);

        var rateLock = Assert.Single(dbContext.RateLocks);
        Assert.Equal(payment.Id, rateLock.PaymentId);
        Assert.Equal("BTC", rateLock.SupportedCurrency);
        Assert.Equal("test-rate-source", rateLock.RateSource);
        Assert.Equal("50000.00", rateLock.RateValue);

        var addressAssignment = Assert.Single(dbContext.PaymentAddressAssignments);
        Assert.Equal(payment.Id, addressAssignment.PaymentId);
        Assert.Equal("BTC", addressAssignment.SupportedCurrency);
        Assert.Equal("bc1qpayaffetestaddress0000000000000000000000000", addressAssignment.PaymentAddress);

        Assert.Contains(
            dbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.currency_selected");
        Assert.Equal(
            ["payment.created", "payment.currency_selected"],
            dbContext.WebhookOutboxEvents
                .OrderBy(webhookEvent => webhookEvent.OccurredAt)
                .Select(webhookEvent => webhookEvent.EventType)
                .ToArray());
    }

    [Fact]
    public async Task Selected_payment_keeps_its_project_policy_after_configuration_changes()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");

        await UpdateConfigurationAsync(requiredConfirmations: 3, tolerancePercent: 2m, reorgDepth: 9);
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var created = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "policy-snapshot",
                PaymentContext: null,
                ReturnUrl: null,
                "policy-snapshot"),
            CancellationToken.None);
        var selected = await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);
        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, selected.Kind);

        await UpdateConfigurationAsync(requiredConfirmations: 1, tolerancePercent: 0m, reorgDepth: 1);
        var observation = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                created.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-policy-snapshot",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 1,
                "test-provider",
                "provider-policy-snapshot"),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Observed, observation.Kind);
        await using var verificationScope = serviceProvider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = await verificationDb.Payments.SingleAsync();
        Assert.Equal(3, payment.ConfirmationRequirement);
        Assert.Equal(2m, payment.PaymentTolerancePercent);
        Assert.Equal(9, payment.ReorgMonitoringDepth);

        async Task UpdateConfigurationAsync(
            int requiredConfirmations,
            decimal tolerancePercent,
            int reorgDepth)
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var configuration = await dbContext.ProjectConfigurations.SingleAsync();
            configuration.BtcConfirmationRequirement = requiredConfirmations;
            configuration.PaymentTolerancePercent = tolerancePercent;
            configuration.BtcReorgMonitoringDepth = reorgDepth;
            await dbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Webhook_delivery_attempt_history_persists_without_raw_secret()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var now = DateTimeOffset.UtcNow;
        var endpointId = Guid.NewGuid();
        dbContext.WebhookEndpoints.Add(new WebhookEndpointRecord
        {
            Id = endpointId,
            IntegrationApiCredentialId = TestIds.CredentialId,
            Url = "https://receiver.example.test/webhooks/payaffe",
            SecretReference = "secret://webhooks/test-endpoint",
            Status = "active",
            EventTypes = "payment.created,payment.currency_selected",
            CreatedAt = now,
            UpdatedAt = now,
        });
        var webhookEvent = Assert.Single(dbContext.WebhookOutboxEvents);
        dbContext.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttemptRecord
        {
            Id = Guid.NewGuid(),
            WebhookEventId = webhookEvent.Id,
            WebhookEndpointId = endpointId,
            AttemptNumber = 1,
            AttemptedAt = now,
            Result = "retry_pending",
            HttpStatusCode = 503,
            SafeErrorCode = "http.5xx",
            NextRetryAt = now.AddMinutes(1),
            CorrelationId = Guid.NewGuid().ToString("D"),
        });

        await dbContext.SaveChangesAsync();

        var endpoint = Assert.Single(dbContext.WebhookEndpoints);
        Assert.Equal("secret://webhooks/test-endpoint", endpoint.SecretReference);
        Assert.DoesNotContain("top-secret", endpoint.SecretReference, StringComparison.Ordinal);

        var attempt = Assert.Single(dbContext.WebhookDeliveryAttempts);
        Assert.Equal(webhookEvent.Id, attempt.WebhookEventId);
        Assert.Equal(endpointId, attempt.WebhookEndpointId);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal("retry_pending", attempt.Result);
        Assert.Equal(503, attempt.HttpStatusCode);
        Assert.Equal("http.5xx", attempt.SafeErrorCode);
    }

    [Fact]
    public async Task Blockchain_observation_persists_matching_transaction_and_observed_event()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);

        var observedAt = DateTimeOffset.Parse("2026-07-04T12:05:00Z");
        var observationResult = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-123",
                "0.00039980",
                observedAt,
                Confirmations: 0,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Observed, observationResult.Kind);
        Assert.Equal("observed", observationResult.Payment!.Status);
        Assert.Equal("0.0003998", observationResult.Payment.ObservedTotal);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(dbContext.Payments);
        Assert.Equal("observed", payment.Status);

        var matchingTransaction = Assert.Single(dbContext.MatchingBlockchainTransactions);
        Assert.Equal(payment.Id, matchingTransaction.PaymentId);
        Assert.Equal("BTC", matchingTransaction.SupportedCurrency);
        Assert.Equal("bc1qpayaffetestaddress0000000000000000000000000", matchingTransaction.PaymentAddress);
        Assert.Equal("tx-123", matchingTransaction.TransactionHash);
        Assert.Equal("0.00039980", matchingTransaction.ObservedAmount);
        Assert.Equal(observedAt, matchingTransaction.ObservedAt);
        Assert.Equal(0, matchingTransaction.Confirmations);
        Assert.Equal("test-provider", matchingTransaction.ProviderName);

        Assert.Contains(
            dbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.observed");
        Assert.Equal(
            ["payment.created", "payment.currency_selected", "payment.observed"],
            dbContext.WebhookOutboxEvents
                .OrderBy(webhookEvent => webhookEvent.OccurredAt)
                .Select(webhookEvent => webhookEvent.EventType)
                .ToArray());
    }

    [Fact]
    public async Task Confirmed_blockchain_observation_completes_payment_and_persists_completion_event()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);

        var observationResult = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-confirmed-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 1,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Completed, observationResult.Kind);
        Assert.Equal("completed", observationResult.Payment!.Status);
        Assert.Equal("0.0003998", observationResult.Payment.ObservedTotal);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), observationResult.Payment.CompletedAt);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(dbContext.Payments);
        Assert.Equal("completed", payment.Status);
        Assert.Equal("0.0003998", payment.ConfirmedEligibleTotal);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), payment.CompletedAt);

        var matchingTransaction = Assert.Single(dbContext.MatchingBlockchainTransactions);
        Assert.True(matchingTransaction.ContributedToCompletion);

        var webhookEventTypes = dbContext.WebhookOutboxEvents
            .Select(webhookEvent => webhookEvent.EventType)
            .ToArray();
        Assert.Contains("payment.created", webhookEventTypes);
        Assert.Contains("payment.currency_selected", webhookEventTypes);
        Assert.Contains("payment.observed", webhookEventTypes);
        Assert.Contains("payment.completed", webhookEventTypes);
        Assert.Equal(4, webhookEventTypes.Length);
        Assert.Contains(
            dbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.completed");
    }

    [Theory]
    [InlineData("0.00030000", "observed", "underpaid", false)]
    [InlineData("0.00050000", "completed", "overpaid", true)]
    public async Task Confirmed_underpayment_and_overpayment_expose_actionable_amount_state(
        string observedAmount,
        string expectedStatus,
        string expectedAmountState,
        bool expectedCompletion)
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                $"order-{expectedAmountState}",
                PaymentContext: null,
                ReturnUrl: null,
                $"create-{expectedAmountState}"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "BTC"),
            CancellationToken.None);

        var result = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "BTC",
                "bc1qpayaffetestaddress0000000000000000000000000",
                $"tx-{expectedAmountState}",
                observedAmount,
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 1,
                "test-provider",
                $"provider-{expectedAmountState}"),
            CancellationToken.None);

        Assert.Equal(expectedCompletion, result.Kind == RecordBlockchainObservationResultKind.Completed);
        Assert.Equal(expectedStatus, result.Payment!.Status);
        Assert.Equal(expectedAmountState, result.Payment.ObservedAmountState);
        Assert.Equal(expectedCompletion, result.Payment.CompletedAt is not null);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Equal(
            expectedCompletion,
            dbContext.WebhookOutboxEvents.Any(webhook => webhook.EventType == "payment.completed"));
    }

    [Fact]
    public async Task Confirmation_update_completes_previously_observed_payment()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);
        await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-later-confirmed-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 0,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);

        var updateResult = await payments.UpdateBlockchainTransactionConfirmationsAsync(
            new UpdateBlockchainTransactionConfirmationsCommand(
                createResult.Payment.PaymentId,
                "btc",
                "tx-later-confirmed-123",
                Confirmations: 1,
                BlockHash: "block-123",
                BlockHeight: 840000),
            CancellationToken.None);

        Assert.Equal(UpdateBlockchainTransactionConfirmationsResultKind.Completed, updateResult.Kind);
        Assert.Equal("completed", updateResult.Payment!.Status);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(dbContext.Payments);
        Assert.Equal("completed", payment.Status);
        Assert.Equal("0.0003998", payment.ConfirmedEligibleTotal);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), payment.CompletedAt);

        var matchingTransaction = Assert.Single(dbContext.MatchingBlockchainTransactions);
        Assert.Equal(1, matchingTransaction.Confirmations);
        Assert.Equal("block-123", matchingTransaction.BlockHash);
        Assert.Equal(840000, matchingTransaction.BlockHeight);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), matchingTransaction.LastCheckedAt);
        Assert.True(matchingTransaction.ContributedToCompletion);

        Assert.Contains(
            dbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.completed");
        Assert.Contains(
            dbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.completed");
    }

    [Fact]
    public async Task Confirmation_update_for_reorg_affected_completed_payment_creates_reorg_alert()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);
        await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-reorg-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 0,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);
        var completionResult = await payments.UpdateBlockchainTransactionConfirmationsAsync(
            new UpdateBlockchainTransactionConfirmationsCommand(
                createResult.Payment.PaymentId,
                "btc",
                "tx-reorg-123",
                Confirmations: 1,
                BlockHash: "block-original",
                BlockHeight: 840000),
            CancellationToken.None);
        Assert.Equal(UpdateBlockchainTransactionConfirmationsResultKind.Completed, completionResult.Kind);

        var reorgResult = await payments.UpdateBlockchainTransactionConfirmationsAsync(
            new UpdateBlockchainTransactionConfirmationsCommand(
                createResult.Payment.PaymentId,
                "btc",
                "tx-reorg-123",
                Confirmations: 0,
                BlockHash: "block-reorged",
                BlockHeight: 840001),
            CancellationToken.None);

        Assert.Equal(UpdateBlockchainTransactionConfirmationsResultKind.ReorgAlerted, reorgResult.Kind);
        Assert.Equal("completed", reorgResult.Payment!.Status);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = Assert.Single(dbContext.Payments);
        Assert.Equal("completed", payment.Status);
        Assert.Equal("0.0003998", payment.ConfirmedEligibleTotal);

        var matchingTransaction = Assert.Single(dbContext.MatchingBlockchainTransactions);
        Assert.True(matchingTransaction.ContributedToCompletion);
        Assert.True(matchingTransaction.ReorgAffected);
        Assert.Equal(0, matchingTransaction.Confirmations);
        Assert.Equal("block-reorged", matchingTransaction.BlockHash);
        Assert.Equal(840001, matchingTransaction.BlockHeight);

        var reorgAlert = Assert.Single(dbContext.ReorgAlerts);
        Assert.Equal(payment.Id, reorgAlert.PaymentId);
        Assert.Equal(matchingTransaction.Id, reorgAlert.MatchingBlockchainTransactionId);
        Assert.Equal("BTC", reorgAlert.SupportedCurrency);
        Assert.Equal("tx-reorg-123", reorgAlert.TransactionHash);
        Assert.Equal(1, reorgAlert.PreviousConfirmations);
        Assert.Equal(0, reorgAlert.NewConfirmations);
        Assert.Equal("block-original", reorgAlert.PreviousBlockHash);
        Assert.Equal("block-reorged", reorgAlert.NewBlockHash);
        Assert.Equal(840000, reorgAlert.PreviousBlockHeight);
        Assert.Equal(840001, reorgAlert.NewBlockHeight);
        Assert.Equal("open", reorgAlert.Status);

        Assert.Contains(
            dbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.reorg_alerted");
        Assert.DoesNotContain(
            dbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.reorg_alerted");
    }

    [Fact]
    public async Task Observation_before_expiration_can_complete_after_expiration_when_confirmed_later()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);

        using (var setupScope = serviceProvider.CreateScope())
        {
            var setupDbContext = setupScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var payment = await setupDbContext.Payments.FindAsync(createResult.Payment!.PaymentId);
            Assert.NotNull(payment);
            payment.ExpiresAt = DateTimeOffset.Parse("2026-07-04T11:00:00Z");
            payment.LateAcceptanceEndsAt = DateTimeOffset.Parse("2026-07-05T11:00:00Z");
            await setupDbContext.SaveChangesAsync();
        }

        using var observationScope = serviceProvider.CreateScope();
        var scopedPayments = observationScope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
        await scopedPayments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-before-expiration-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T10:55:00Z"),
                Confirmations: 0,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);

        var updateResult = await scopedPayments.UpdateBlockchainTransactionConfirmationsAsync(
            new UpdateBlockchainTransactionConfirmationsCommand(
                createResult.Payment.PaymentId,
                "btc",
                "tx-before-expiration-123",
                Confirmations: 1,
                BlockHash: "block-123",
                BlockHeight: 840000),
            CancellationToken.None);

        Assert.Equal(UpdateBlockchainTransactionConfirmationsResultKind.Completed, updateResult.Kind);
        Assert.Equal("completed", updateResult.Payment!.Status);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var paymentRecord = Assert.Single(dbContext.Payments);
        Assert.Equal("completed", paymentRecord.Status);
        Assert.Equal("0.0003998", paymentRecord.ConfirmedEligibleTotal);

        var matchingTransaction = Assert.Single(dbContext.MatchingBlockchainTransactions);
        Assert.True(matchingTransaction.ContributedToCompletion);
    }

    [Fact]
    public async Task Confirmed_observation_within_late_acceptance_window_completes_payment()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);

        using (var setupScope = serviceProvider.CreateScope())
        {
            var setupDbContext = setupScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var payment = await setupDbContext.Payments.FindAsync(createResult.Payment!.PaymentId);
            Assert.NotNull(payment);
            payment.ExpiresAt = DateTimeOffset.Parse("2026-07-04T11:00:00Z");
            payment.LateAcceptanceEndsAt = DateTimeOffset.Parse("2026-07-04T12:30:00Z");
            await setupDbContext.SaveChangesAsync();
        }

        using var observationScope = serviceProvider.CreateScope();
        var scopedPayments = observationScope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
        var observationResult = await scopedPayments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-within-late-window-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 1,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Completed, observationResult.Kind);
        Assert.Equal("completed", observationResult.Payment!.Status);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var paymentRecord = Assert.Single(dbContext.Payments);
        Assert.Equal("completed", paymentRecord.Status);
        Assert.Equal("0.0003998", paymentRecord.ConfirmedEligibleTotal);
        Assert.Contains(
            dbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.completed");
    }

    [Fact]
    public async Task Confirmed_observation_at_late_acceptance_boundary_completes_payment()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);

        using (var setupScope = serviceProvider.CreateScope())
        {
            var setupDbContext = setupScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var payment = await setupDbContext.Payments.FindAsync(createResult.Payment!.PaymentId);
            Assert.NotNull(payment);
            payment.ExpiresAt = DateTimeOffset.Parse("2026-07-04T11:00:00Z");
            payment.LateAcceptanceEndsAt = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
            await setupDbContext.SaveChangesAsync();
        }

        using var observationScope = serviceProvider.CreateScope();
        var scopedPayments = observationScope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
        var observationResult = await scopedPayments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-at-late-window-boundary-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:00:00Z"),
                Confirmations: 1,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Completed, observationResult.Kind);
        Assert.Equal("completed", observationResult.Payment!.Status);
    }

    [Fact]
    public async Task Confirmed_observation_after_late_acceptance_window_is_not_completed_automatically()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);
        await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("fixed-payer-page-id", "btc"),
            CancellationToken.None);

        using (var setupScope = serviceProvider.CreateScope())
        {
            var setupDbContext = setupScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var payment = await setupDbContext.Payments.FindAsync(createResult.Payment!.PaymentId);
            Assert.NotNull(payment);
            payment.ExpiresAt = DateTimeOffset.Parse("2026-07-04T10:00:00Z");
            payment.LateAcceptanceEndsAt = DateTimeOffset.Parse("2026-07-04T11:00:00Z");
            await setupDbContext.SaveChangesAsync();
        }

        using var observationScope = serviceProvider.CreateScope();
        var scopedPayments = observationScope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
        var observationResult = await scopedPayments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                createResult.Payment!.PaymentId,
                "btc",
                "bc1qpayaffetestaddress0000000000000000000000000",
                "tx-too-late-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T11:00:01Z"),
                Confirmations: 1,
                "test-provider",
                "provider-observation-123"),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Observed, observationResult.Kind);
        Assert.Equal("observed", observationResult.Payment!.Status);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var paymentRecord = Assert.Single(dbContext.Payments);
        Assert.Equal("observed", paymentRecord.Status);
        Assert.Null(paymentRecord.CompletedAt);
        Assert.Null(paymentRecord.ConfirmedEligibleTotal);

        var matchingTransaction = Assert.Single(dbContext.MatchingBlockchainTransactions);
        Assert.False(matchingTransaction.ContributedToCompletion);
        Assert.DoesNotContain(
            dbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.completed");
    }

    [Fact]
    public async Task Expire_due_payments_marks_late_window_elapsed_payments_expired()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var serviceProvider = BuildMigratedServiceProvider(connectionString);
        await SeedCredentialAsync(serviceProvider, "valid-token");
        var payments = serviceProvider.GetRequiredService<PaymentApplicationService>();
        var createResult = await payments.CreateAsync(
            TestIds.CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-123"),
            CancellationToken.None);

        using (var setupScope = serviceProvider.CreateScope())
        {
            var setupDbContext = setupScope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var payment = await setupDbContext.Payments.FindAsync(createResult.Payment!.PaymentId);
            Assert.NotNull(payment);
            payment.LateAcceptanceEndsAt = DateTimeOffset.Parse("2026-07-04T11:59:00Z");
            await setupDbContext.SaveChangesAsync();
        }

        var result = await payments.ExpireDuePaymentsAsync(10, CancellationToken.None);

        Assert.Equal(1, result.ExpiredCount);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var expiredPayment = Assert.Single(dbContext.Payments);
        Assert.Equal("expired", expiredPayment.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), expiredPayment.UpdatedAt);
        Assert.Contains(
            dbContext.PaymentEventHistory,
            paymentEvent => paymentEvent.EventType == "payment.expired");
        var webhookEvent = Assert.Single(
            dbContext.WebhookOutboxEvents,
            webhookEvent => webhookEvent.EventType == "payment.expired");
        Assert.Equal("pending", webhookEvent.Status);
        Assert.Equal(expiredPayment.Id.ToString("D"), webhookEvent.ResourceId);
    }

    private static ServiceProvider BuildMigratedServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString);
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.AddScoped<IExchangeRateSource, FixedExchangeRateSource>();
        services.AddScoped<IPaymentAddressProvider, FixedPaymentAddressProvider>();
        services.AddScoped<IBlockchainObservationAdapter, NoOpBlockchainObservationAdapter>();
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PayerPageBaseUrl = "https://pay.example.test/pay";
            options.PaymentExpiration = TimeSpan.FromHours(1);
            options.LateAcceptanceWindow = TimeSpan.FromHours(24);
        });

        var serviceProvider = services.BuildServiceProvider();
        MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None).GetAwaiter().GetResult();
        return serviceProvider;
    }

    private static async Task SeedCredentialAsync(IServiceProvider serviceProvider, string token)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var now = DateTimeOffset.UtcNow;

        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = TestIds.CredentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken(token),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task<List<string>> QueryListAsync(string connectionString, string sql)
    {
        var values = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<Dictionary<string, string>> QueryDictionaryAsync(string connectionString, string sql)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    private static class TestIds
    {
        public static readonly Guid CredentialId = Guid.Parse("92881f34-1c04-4af2-b2d0-15aec72d88e2");
    }

    private sealed class FixedExchangeRateSource : IExchangeRateSource
    {
        public Task<RateLockQuote?> GetRateLockQuoteAsync(
            string fiatCurrency,
            long fiatAmountMinor,
            string supportedCurrency,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<RateLockQuote?>(new RateLockQuote(
                supportedCurrency,
                "test-rate-source",
                "50000.00",
                "0.00039980",
                requestedAt));
        }
    }

    private sealed class FixedPayerPageIdGenerator : IPayerPageIdGenerator
    {
        public string Generate() => "fixed-payer-page-id";
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
    }

    private sealed class FixedPaymentAddressProvider : IPaymentAddressProvider
    {
        public Task<PaymentAddressAssignment?> AssignAsync(
            Guid projectId,
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken)
        {
            var assignment = supportedCurrency switch
            {
                "BTC" => new PaymentAddressAssignment(
                    "BTC",
                    "bc1qpayaffetestaddress0000000000000000000000000",
                    "mainnet"),
                "LTC" => new PaymentAddressAssignment(
                    "LTC",
                    "ltc1qpayaffetestaddress000000000000000000000000",
                    "mainnet"),
                "ETH" => new PaymentAddressAssignment(
                    "ETH",
                    "0x1111111111111111111111111111111111111111",
                    "mainnet",
                    1),
                _ => throw new ArgumentOutOfRangeException(nameof(supportedCurrency)),
            };
            return Task.FromResult<PaymentAddressAssignment?>(assignment);
        }
    }
}
