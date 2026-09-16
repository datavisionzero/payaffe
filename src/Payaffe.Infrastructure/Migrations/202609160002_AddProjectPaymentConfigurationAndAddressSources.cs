using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payaffe.Infrastructure.Persistence;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202609160002_AddProjectPaymentConfigurationAndAddressSources")]
public partial class AddProjectPaymentConfigurationAndAddressSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE app.project_configuration ADD COLUMN btc_enabled boolean NOT NULL DEFAULT true;
            ALTER TABLE app.project_configuration ADD COLUMN ltc_enabled boolean NOT NULL DEFAULT true;
            ALTER TABLE app.project_configuration ADD COLUMN eth_enabled boolean NOT NULL DEFAULT true;

            ALTER TABLE app.payments ADD COLUMN confirmation_requirement integer NULL;
            ALTER TABLE app.payments ADD COLUMN payment_tolerance_percent numeric NULL;
            ALTER TABLE app.payments ADD COLUMN reorg_monitoring_depth integer NULL;

            ALTER TABLE app.payment_address_assignments ADD COLUMN source_fingerprint text NULL;
            ALTER TABLE app.payment_address_assignments ADD COLUMN derivation_index bigint NULL;
            CREATE UNIQUE INDEX uq_payment_address_assignments_currency_address
                ON app.payment_address_assignments (supported_currency, payment_address)
                WHERE source_fingerprint IS NOT NULL;

            ALTER TABLE app.watch_only_wallet_cursors DROP CONSTRAINT pk_watch_only_wallet_cursors;
            ALTER TABLE app.watch_only_wallet_cursors
                ADD CONSTRAINT pk_watch_only_wallet_cursors PRIMARY KEY (supported_currency, source_fingerprint);

            CREATE TABLE app.project_watch_only_wallet_sources (
                project_id uuid NOT NULL,
                supported_currency text NOT NULL,
                source_fingerprint text NOT NULL,
                network text NOT NULL,
                address_type text NOT NULL,
                starting_index bigint NOT NULL,
                source_reference text NOT NULL,
                enabled boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                version bigint NOT NULL DEFAULT 1,
                CONSTRAINT pk_project_watch_only_wallet_sources PRIMARY KEY (project_id, supported_currency),
                CONSTRAINT fk_project_watch_only_wallet_sources_projects FOREIGN KEY (project_id) REFERENCES app.projects (id) ON DELETE RESTRICT,
                CONSTRAINT ck_project_watch_only_wallet_sources_currency CHECK (supported_currency in ('BTC', 'LTC')),
                CONSTRAINT ck_project_watch_only_wallet_sources_starting_index CHECK (starting_index >= 0 and starting_index <= 2147483647)
            );
            CREATE INDEX ix_project_watch_only_wallet_sources_fingerprint
                ON app.project_watch_only_wallet_sources (supported_currency, source_fingerprint);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE app.project_watch_only_wallet_sources;
            ALTER TABLE app.watch_only_wallet_cursors DROP CONSTRAINT pk_watch_only_wallet_cursors;
            ALTER TABLE app.watch_only_wallet_cursors
                ADD CONSTRAINT pk_watch_only_wallet_cursors PRIMARY KEY (supported_currency);
            DROP INDEX app.uq_payment_address_assignments_currency_address;
            ALTER TABLE app.payment_address_assignments DROP COLUMN derivation_index;
            ALTER TABLE app.payment_address_assignments DROP COLUMN source_fingerprint;
            ALTER TABLE app.payments DROP COLUMN reorg_monitoring_depth;
            ALTER TABLE app.payments DROP COLUMN payment_tolerance_percent;
            ALTER TABLE app.payments DROP COLUMN confirmation_requirement;
            ALTER TABLE app.project_configuration DROP COLUMN eth_enabled;
            ALTER TABLE app.project_configuration DROP COLUMN ltc_enabled;
            ALTER TABLE app.project_configuration DROP COLUMN btc_enabled;
            """);
    }
}
