using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payaffe.Infrastructure.Persistence;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202609180001_AddPaymentInstructionNetwork")]
public partial class AddPaymentInstructionNetwork : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE app.payment_address_assignments ADD COLUMN network text NULL;
            ALTER TABLE app.payment_address_assignments ADD COLUMN chain_id bigint NULL;

            UPDATE app.payment_address_assignments assignment
            SET network = CASE
                    WHEN assignment.supported_currency = 'BTC'
                         AND (
                             lower(assignment.payment_address) LIKE 'tb1%'
                             OR assignment.payment_address LIKE 'm%'
                             OR assignment.payment_address LIKE 'n%'
                             OR assignment.payment_address LIKE '2%'
                         ) THEN 'testnet'
                    WHEN assignment.supported_currency = 'LTC'
                         AND (
                             lower(assignment.payment_address) LIKE 'tltc1%'
                             OR assignment.payment_address LIKE 'm%'
                             OR assignment.payment_address LIKE 'n%'
                             OR assignment.payment_address LIKE 'Q%'
                         ) THEN 'testnet'
                    ELSE 'mainnet'
                END,
                chain_id = CASE
                    WHEN assignment.supported_currency = 'ETH' THEN 1
                    ELSE NULL
                END;

            ALTER TABLE app.payment_address_assignments ALTER COLUMN network SET NOT NULL;
            ALTER TABLE app.payment_address_assignments
                ADD CONSTRAINT ck_payment_address_assignments_network
                CHECK (network in ('mainnet', 'testnet'));
            ALTER TABLE app.payment_address_assignments
                ADD CONSTRAINT ck_payment_address_assignments_chain_id
                CHECK (
                    (supported_currency = 'ETH' and chain_id is not null and chain_id > 0)
                    or (supported_currency <> 'ETH' and chain_id is null));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE app.payment_address_assignments
                DROP CONSTRAINT ck_payment_address_assignments_chain_id;
            ALTER TABLE app.payment_address_assignments
                DROP CONSTRAINT ck_payment_address_assignments_network;
            ALTER TABLE app.payment_address_assignments DROP COLUMN chain_id;
            ALTER TABLE app.payment_address_assignments DROP COLUMN network;
            """);
    }
}
