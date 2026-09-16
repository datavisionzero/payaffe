using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202609160001_AddProjectOwnership")]
public partial class AddProjectOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE app.projects (
                id uuid NOT NULL,
                name text NOT NULL,
                slug text NOT NULL,
                status text NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                version bigint NOT NULL DEFAULT 1,
                CONSTRAINT pk_projects PRIMARY KEY (id),
                CONSTRAINT uq_projects_slug UNIQUE (slug),
                CONSTRAINT ck_projects_status CHECK (status in ('active', 'disabled', 'archived'))
            );

            INSERT INTO app.projects (id, name, slug, status, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-000000000001', 'Default Project', 'default', 'active', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

            CREATE TABLE app.project_configuration (
                project_id uuid NOT NULL,
                payment_expiration_seconds bigint NOT NULL DEFAULT 3600,
                late_acceptance_window_seconds bigint NOT NULL DEFAULT 86400,
                payment_tolerance_percent numeric NOT NULL DEFAULT 1.0,
                btc_confirmation_requirement integer NOT NULL DEFAULT 1,
                ltc_confirmation_requirement integer NOT NULL DEFAULT 1,
                eth_confirmation_requirement integer NOT NULL DEFAULT 12,
                btc_reorg_monitoring_depth integer NOT NULL DEFAULT 6,
                ltc_reorg_monitoring_depth integer NOT NULL DEFAULT 12,
                eth_reorg_monitoring_depth integer NOT NULL DEFAULT 64,
                native_eth_low_capacity_threshold integer NOT NULL DEFAULT 20,
                legacy_settings_fingerprint text NOT NULL DEFAULT '',
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                version bigint NOT NULL DEFAULT 1,
                CONSTRAINT pk_project_configuration PRIMARY KEY (project_id),
                CONSTRAINT fk_project_configuration_projects FOREIGN KEY (project_id) REFERENCES app.projects (id) ON DELETE RESTRICT,
                CONSTRAINT ck_project_configuration_payment_expiration CHECK (payment_expiration_seconds > 0),
                CONSTRAINT ck_project_configuration_late_acceptance CHECK (late_acceptance_window_seconds >= 0),
                CONSTRAINT ck_project_configuration_tolerance CHECK (payment_tolerance_percent >= 0 and payment_tolerance_percent <= 100),
                CONSTRAINT ck_project_configuration_confirmations CHECK (btc_confirmation_requirement >= 0 and ltc_confirmation_requirement >= 0 and eth_confirmation_requirement >= 0),
                CONSTRAINT ck_project_configuration_reorg_depths CHECK (btc_reorg_monitoring_depth >= 0 and ltc_reorg_monitoring_depth >= 0 and eth_reorg_monitoring_depth >= 0),
                CONSTRAINT ck_project_configuration_eth_threshold CHECK (native_eth_low_capacity_threshold >= 0)
            );

            INSERT INTO app.project_configuration (project_id, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-000000000001', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

            ALTER TABLE auth.integration_api_credentials ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.payments ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.payment_options ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.payment_creation_idempotency ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.payment_event_history ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.rate_locks ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.payment_address_assignments ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.native_eth_address_pool_imports ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.native_eth_addresses ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.matching_blockchain_transactions ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.reorg_alerts ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE app.webhook_endpoints ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE outbox.webhook_events ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE outbox.webhook_delivery_attempts ADD COLUMN project_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001';
            ALTER TABLE audit.audit_log_entries ADD COLUMN project_id uuid NULL;

            ALTER TABLE app.payments DROP CONSTRAINT fk_payments_integration_api_credentials;
            ALTER TABLE app.payment_options DROP CONSTRAINT fk_payment_options_payments;
            ALTER TABLE app.payment_creation_idempotency DROP CONSTRAINT fk_payment_creation_idempotency_integration_api_credentials;
            ALTER TABLE app.payment_creation_idempotency DROP CONSTRAINT fk_payment_creation_idempotency_payments;
            ALTER TABLE app.payment_event_history DROP CONSTRAINT fk_payment_event_history_payments;
            ALTER TABLE app.rate_locks DROP CONSTRAINT fk_rate_locks_payments;
            ALTER TABLE app.payment_address_assignments DROP CONSTRAINT fk_payment_address_assignments_payments;
            ALTER TABLE app.native_eth_addresses DROP CONSTRAINT fk_native_eth_addresses_imports;
            ALTER TABLE app.native_eth_addresses DROP CONSTRAINT fk_native_eth_addresses_assigned_payments;
            ALTER TABLE app.matching_blockchain_transactions DROP CONSTRAINT fk_matching_blockchain_transactions_payments;
            ALTER TABLE app.reorg_alerts DROP CONSTRAINT fk_reorg_alerts_payments;
            ALTER TABLE app.reorg_alerts DROP CONSTRAINT fk_reorg_alerts_matching_blockchain_transactions;
            ALTER TABLE app.webhook_endpoints DROP CONSTRAINT fk_webhook_endpoints_integration_api_credentials;
            ALTER TABLE outbox.webhook_events DROP CONSTRAINT fk_webhook_events_payments;
            ALTER TABLE outbox.webhook_events DROP CONSTRAINT fk_webhook_events_integration_api_credentials;
            ALTER TABLE outbox.webhook_delivery_attempts DROP CONSTRAINT fk_webhook_delivery_attempts_webhook_events;
            ALTER TABLE outbox.webhook_delivery_attempts DROP CONSTRAINT fk_webhook_delivery_attempts_webhook_endpoints;

            ALTER TABLE auth.integration_api_credentials ADD CONSTRAINT ak_integration_api_credentials_project_id_id UNIQUE (project_id, id);
            ALTER TABLE app.payments ADD CONSTRAINT ak_payments_project_id_id UNIQUE (project_id, id);
            ALTER TABLE app.native_eth_address_pool_imports ADD CONSTRAINT ak_native_eth_address_pool_imports_project_id_id UNIQUE (project_id, id);
            ALTER TABLE app.matching_blockchain_transactions ADD CONSTRAINT ak_matching_blockchain_transactions_project_id_id UNIQUE (project_id, id);
            ALTER TABLE app.webhook_endpoints ADD CONSTRAINT ak_webhook_endpoints_project_id_id UNIQUE (project_id, id);
            ALTER TABLE outbox.webhook_events ADD CONSTRAINT ak_webhook_events_project_id_id UNIQUE (project_id, id);

            ALTER TABLE auth.integration_api_credentials ADD CONSTRAINT fk_integration_api_credentials_projects FOREIGN KEY (project_id) REFERENCES app.projects (id) ON DELETE RESTRICT;
            ALTER TABLE app.payments ADD CONSTRAINT fk_payments_integration_api_credentials FOREIGN KEY (project_id, integration_api_credential_id) REFERENCES auth.integration_api_credentials (project_id, id) ON DELETE RESTRICT;
            ALTER TABLE app.payment_options ADD CONSTRAINT fk_payment_options_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.payment_creation_idempotency ADD CONSTRAINT fk_payment_creation_idempotency_integration_api_credentials FOREIGN KEY (project_id, integration_api_credential_id) REFERENCES auth.integration_api_credentials (project_id, id) ON DELETE RESTRICT;
            ALTER TABLE app.payment_creation_idempotency ADD CONSTRAINT fk_payment_creation_idempotency_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.payment_event_history ADD CONSTRAINT fk_payment_event_history_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.rate_locks ADD CONSTRAINT fk_rate_locks_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.payment_address_assignments ADD CONSTRAINT fk_payment_address_assignments_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.native_eth_address_pool_imports ADD CONSTRAINT fk_native_eth_address_pool_imports_projects FOREIGN KEY (project_id) REFERENCES app.projects (id) ON DELETE RESTRICT;
            ALTER TABLE app.native_eth_addresses ADD CONSTRAINT fk_native_eth_addresses_imports FOREIGN KEY (project_id, import_id) REFERENCES app.native_eth_address_pool_imports (project_id, id) ON DELETE RESTRICT;
            ALTER TABLE app.native_eth_addresses ADD CONSTRAINT fk_native_eth_addresses_assigned_payments FOREIGN KEY (project_id, assigned_payment_id) REFERENCES app.payments (project_id, id) ON DELETE RESTRICT;
            ALTER TABLE app.matching_blockchain_transactions ADD CONSTRAINT fk_matching_blockchain_transactions_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.reorg_alerts ADD CONSTRAINT fk_reorg_alerts_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.reorg_alerts ADD CONSTRAINT fk_reorg_alerts_matching_blockchain_transactions FOREIGN KEY (project_id, matching_blockchain_transaction_id) REFERENCES app.matching_blockchain_transactions (project_id, id) ON DELETE CASCADE;
            ALTER TABLE app.webhook_endpoints ADD CONSTRAINT fk_webhook_endpoints_integration_api_credentials FOREIGN KEY (project_id, integration_api_credential_id) REFERENCES auth.integration_api_credentials (project_id, id) ON DELETE RESTRICT;
            ALTER TABLE outbox.webhook_events ADD CONSTRAINT fk_webhook_events_payments FOREIGN KEY (project_id, payment_id) REFERENCES app.payments (project_id, id) ON DELETE CASCADE;
            ALTER TABLE outbox.webhook_events ADD CONSTRAINT fk_webhook_events_integration_api_credentials FOREIGN KEY (project_id, integration_api_credential_id) REFERENCES auth.integration_api_credentials (project_id, id) ON DELETE RESTRICT;
            ALTER TABLE outbox.webhook_delivery_attempts ADD CONSTRAINT fk_webhook_delivery_attempts_webhook_events FOREIGN KEY (project_id, webhook_event_id) REFERENCES outbox.webhook_events (project_id, id) ON DELETE CASCADE;
            ALTER TABLE outbox.webhook_delivery_attempts ADD CONSTRAINT fk_webhook_delivery_attempts_webhook_endpoints FOREIGN KEY (project_id, webhook_endpoint_id) REFERENCES app.webhook_endpoints (project_id, id) ON DELETE RESTRICT;
            ALTER TABLE audit.audit_log_entries ADD CONSTRAINT fk_audit_log_entries_projects FOREIGN KEY (project_id) REFERENCES app.projects (id) ON DELETE RESTRICT;

            DROP INDEX app.uq_payment_creation_idempotency_credential_key;
            DROP INDEX app.ix_payment_event_history_payment_id;
            DROP INDEX app.ix_payments_integration_api_credential_id;
            DROP INDEX app.ix_native_eth_addresses_status_created_at;
            DROP INDEX app.ix_matching_blockchain_transactions_payment_id;
            DROP INDEX app.ix_reorg_alerts_matching_blockchain_transaction_id;
            DROP INDEX app.ix_reorg_alerts_payment_id;
            DROP INDEX app.ix_webhook_endpoints_integration_api_credential_id;
            DROP INDEX outbox.ix_webhook_events_status_next_attempt_at;
            DROP INDEX outbox.ix_webhook_delivery_attempts_webhook_endpoint_id;
            DROP INDEX outbox.ix_webhook_delivery_attempts_webhook_event_id;

            CREATE INDEX ix_integration_api_credentials_project_status ON auth.integration_api_credentials (project_id, status);
            CREATE INDEX ix_payments_project_credential ON app.payments (project_id, integration_api_credential_id);
            CREATE INDEX ix_payments_project_status_updated_at ON app.payments (project_id, status, updated_at);
            CREATE INDEX ix_payment_options_project_payment ON app.payment_options (project_id, payment_id);
            CREATE UNIQUE INDEX uq_payment_creation_idempotency_project_credential_key ON app.payment_creation_idempotency (project_id, integration_api_credential_id, idempotency_key);
            CREATE INDEX ix_payment_event_history_project_payment_occurred_at ON app.payment_event_history (project_id, payment_id, occurred_at);
            CREATE INDEX ix_payment_address_assignments_project_payment ON app.payment_address_assignments (project_id, payment_id);
            CREATE INDEX ix_native_eth_address_pool_imports_project_imported_at ON app.native_eth_address_pool_imports (project_id, imported_at);
            CREATE INDEX ix_native_eth_addresses_project_status_created_at ON app.native_eth_addresses (project_id, status, created_at);
            CREATE INDEX ix_matching_blockchain_transactions_project_payment ON app.matching_blockchain_transactions (project_id, payment_id);
            CREATE INDEX ix_reorg_alerts_project_payment ON app.reorg_alerts (project_id, payment_id);
            CREATE INDEX ix_reorg_alerts_project_matching_transaction ON app.reorg_alerts (project_id, matching_blockchain_transaction_id);
            CREATE INDEX ix_webhook_endpoints_project_credential ON app.webhook_endpoints (project_id, integration_api_credential_id);
            CREATE INDEX ix_webhook_events_project_status_next_attempt_at ON outbox.webhook_events (project_id, status, next_attempt_at);
            CREATE INDEX ix_webhook_delivery_attempts_project_event ON outbox.webhook_delivery_attempts (project_id, webhook_event_id);
            CREATE INDEX ix_webhook_delivery_attempts_project_endpoint ON outbox.webhook_delivery_attempts (project_id, webhook_endpoint_id);
            CREATE INDEX ix_audit_log_entries_project_occurred_at ON audit.audit_log_entries (project_id, occurred_at);

            INSERT INTO audit.audit_log_entries (
                event_id, project_id, occurred_at, event_type, outcome, actor_type, actor_id,
                source_service, correlation_id, reason_code, subject_type, subject_id)
            VALUES (
                '00000000-0000-0000-0000-000000000002',
                '00000000-0000-0000-0000-000000000001',
                CURRENT_TIMESTAMP,
                'project.default_migrated',
                'success',
                'system',
                'schema-migration',
                'migrations',
                'schema:default-project',
                'project.default_migrated',
                'project',
                '00000000-0000-0000-0000-000000000001');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM audit.audit_log_entries WHERE event_id = '00000000-0000-0000-0000-000000000002';
            ALTER TABLE audit.audit_log_entries DROP CONSTRAINT fk_audit_log_entries_projects;
            ALTER TABLE audit.audit_log_entries DROP COLUMN project_id;
            DROP TABLE app.project_configuration;
            DROP TABLE app.projects CASCADE;
            """);
    }
}
