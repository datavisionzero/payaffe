using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607050005_AddReorgAlerts")]
public partial class AddReorgAlerts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "reorg_affected",
            schema: "app",
            table: "matching_blockchain_transactions",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "reorg_alerts",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                matching_blockchain_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                supported_currency = table.Column<string>(type: "text", nullable: false),
                transaction_hash = table.Column<string>(type: "text", nullable: false),
                previous_confirmations = table.Column<int>(type: "integer", nullable: false),
                new_confirmations = table.Column<int>(type: "integer", nullable: false),
                previous_block_hash = table.Column<string>(type: "text", nullable: true),
                new_block_hash = table.Column<string>(type: "text", nullable: true),
                previous_block_height = table.Column<long>(type: "bigint", nullable: true),
                new_block_height = table.Column<long>(type: "bigint", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reorg_alerts", x => x.id);
                table.ForeignKey(
                    name: "fk_reorg_alerts_matching_blockchain_transactions",
                    column: x => x.matching_blockchain_transaction_id,
                    principalSchema: "app",
                    principalTable: "matching_blockchain_transactions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_reorg_alerts_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("ck_reorg_alerts_new_confirmations", "new_confirmations >= 0");
                table.CheckConstraint("ck_reorg_alerts_previous_confirmations", "previous_confirmations >= 0");
                table.CheckConstraint("ck_reorg_alerts_status", "status in ('open', 'resolved')");
            });

        migrationBuilder.CreateIndex(
            name: "ix_reorg_alerts_matching_blockchain_transaction_id",
            schema: "app",
            table: "reorg_alerts",
            column: "matching_blockchain_transaction_id");

        migrationBuilder.CreateIndex(
            name: "ix_reorg_alerts_payment_id",
            schema: "app",
            table: "reorg_alerts",
            column: "payment_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "reorg_alerts",
            schema: "app");

        migrationBuilder.DropColumn(
            name: "reorg_affected",
            schema: "app",
            table: "matching_blockchain_transactions");
    }
}
