using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(Persistence.PayaffeDbContext))]
[Migration("202609240011_KeyMatchingTransactionsByPayment")]
public sealed class KeyMatchingTransactionsByPayment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // One Blockchain Transaction can pay several Payment Addresses, for
        // example a batched exchange withdrawal, in one Project or across
        // Projects. It is a Matching Blockchain Transaction of each Payment it
        // pays, so it is unique per Payment rather than per installation.
        migrationBuilder.Sql(
            """
            DROP INDEX app.uq_matching_blockchain_transactions_currency_hash;

            CREATE UNIQUE INDEX uq_matching_blockchain_transactions_payment_currency_hash
                ON app.matching_blockchain_transactions (project_id, payment_id, supported_currency, transaction_hash);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX app.uq_matching_blockchain_transactions_payment_currency_hash;

            CREATE UNIQUE INDEX uq_matching_blockchain_transactions_currency_hash
                ON app.matching_blockchain_transactions (supported_currency, transaction_hash);
            """);
}
