using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payaffe.Infrastructure.Persistence;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202609230002_AddSimulatedTransactions")]
public partial class AddSimulatedTransactions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Created in every installation so the schema does not depend on the
        // mode; only a Test Mode installation ever writes to it (ADR 0033).
        migrationBuilder.Sql(
            """
            CREATE TABLE app.simulated_transactions (
                project_id uuid NOT NULL,
                id uuid NOT NULL,
                payment_id uuid NOT NULL,
                supported_currency text NOT NULL,
                payment_address text NOT NULL,
                transaction_hash text NOT NULL,
                amount text NOT NULL,
                observed_at timestamp with time zone NOT NULL,
                first_reported_at timestamp with time zone NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_simulated_transactions PRIMARY KEY (id),
                CONSTRAINT fk_simulated_transactions_payments FOREIGN KEY (project_id, payment_id)
                    REFERENCES app.payments (project_id, id) ON DELETE CASCADE,
                CONSTRAINT ck_simulated_transactions_supported_currency
                    CHECK (supported_currency in ('BTC', 'LTC', 'ETH'))
            );

            CREATE INDEX ix_simulated_transactions_project_payment
                ON app.simulated_transactions (project_id, payment_id);
            CREATE UNIQUE INDEX uq_simulated_transactions_transaction_hash
                ON app.simulated_transactions (transaction_hash);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE app.simulated_transactions;");
}
