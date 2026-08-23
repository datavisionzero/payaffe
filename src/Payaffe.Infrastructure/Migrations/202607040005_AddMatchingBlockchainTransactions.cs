using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607040005_AddMatchingBlockchainTransactions")]
public partial class AddMatchingBlockchainTransactions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "matching_blockchain_transactions",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                supported_currency = table.Column<string>(type: "text", nullable: false),
                payment_address = table.Column<string>(type: "text", nullable: false),
                transaction_hash = table.Column<string>(type: "text", nullable: false),
                observed_amount = table.Column<string>(type: "text", nullable: false),
                observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                first_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                confirmations = table.Column<int>(type: "integer", nullable: false),
                provider_name = table.Column<string>(type: "text", nullable: false),
                provider_observation_id = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_matching_blockchain_transactions", x => x.id);
                table.CheckConstraint("ck_matching_blockchain_transactions_confirmations", "confirmations >= 0");
                table.ForeignKey(
                    name: "fk_matching_blockchain_transactions_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_matching_blockchain_transactions_payment_id",
            schema: "app",
            table: "matching_blockchain_transactions",
            column: "payment_id");

        migrationBuilder.CreateIndex(
            name: "uq_matching_blockchain_transactions_currency_hash",
            schema: "app",
            table: "matching_blockchain_transactions",
            columns: ["supported_currency", "transaction_hash"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "matching_blockchain_transactions", schema: "app");
    }
}
