using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(Persistence.PayaffeDbContext))]
[Migration("202609240013_AddAddressHistoryAlerts")]
public sealed class AddAddressHistoryAlerts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A transaction observed well before currency selection does not
        // count toward the Payment (ADR 0035). Its Observation Time is compared
        // with the moment of selection, and each such transaction is recorded
        // once as an Address History Alert for the operator.
        migrationBuilder.Sql(
            """
            ALTER TABLE app.payments ADD COLUMN currency_selected_at timestamp with time zone NULL;

            UPDATE app.payments AS payment
            SET currency_selected_at = rate_lock.created_at
            FROM app.rate_locks AS rate_lock
            WHERE rate_lock.project_id = payment.project_id
              AND rate_lock.payment_id = payment.id;

            CREATE TABLE app.address_history_alerts (
                project_id uuid NOT NULL,
                id uuid NOT NULL,
                payment_id uuid NOT NULL,
                supported_currency text NOT NULL,
                payment_address text NOT NULL,
                transaction_hash text NOT NULL,
                observed_amount text NOT NULL,
                observed_at timestamp with time zone NOT NULL,
                currency_selected_at timestamp with time zone NOT NULL,
                status text NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                version bigint NOT NULL DEFAULT 1,
                CONSTRAINT pk_address_history_alerts PRIMARY KEY (id),
                CONSTRAINT fk_address_history_alerts_payments FOREIGN KEY (project_id, payment_id)
                    REFERENCES app.payments (project_id, id) ON DELETE CASCADE,
                CONSTRAINT ck_address_history_alerts_status CHECK (status in ('open', 'resolved'))
            );

            CREATE UNIQUE INDEX uq_address_history_alerts_payment_currency_hash
                ON app.address_history_alerts (project_id, payment_id, supported_currency, transaction_hash);
            CREATE INDEX ix_address_history_alerts_project_created_at
                ON app.address_history_alerts (project_id, created_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE app.address_history_alerts;
            ALTER TABLE app.payments DROP COLUMN currency_selected_at;
            """);
}
