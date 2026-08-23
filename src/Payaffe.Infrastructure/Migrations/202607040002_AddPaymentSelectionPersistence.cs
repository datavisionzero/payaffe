using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607040002_AddPaymentSelectionPersistence")]
public partial class AddPaymentSelectionPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "expected_crypto_amount",
            schema: "app",
            table: "payments",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "payment_address",
            schema: "app",
            table: "payments",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "selected_currency",
            schema: "app",
            table: "payments",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "payment_address_assignments",
            schema: "app",
            columns: table => new
            {
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                supported_currency = table.Column<string>(type: "text", nullable: false),
                payment_address = table.Column<string>(type: "text", nullable: false),
                assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payment_address_assignments", x => x.payment_id);
                table.ForeignKey(
                    name: "fk_payment_address_assignments_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "rate_locks",
            schema: "app",
            columns: table => new
            {
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                supported_currency = table.Column<string>(type: "text", nullable: false),
                fiat_currency = table.Column<string>(type: "text", nullable: false),
                fiat_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                expected_crypto_amount = table.Column<string>(type: "text", nullable: false),
                rate_source = table.Column<string>(type: "text", nullable: false),
                rate_value = table.Column<string>(type: "text", nullable: false),
                rate_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_rate_locks", x => x.payment_id);
                table.ForeignKey(
                    name: "fk_rate_locks_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "payment_address_assignments", schema: "app");
        migrationBuilder.DropTable(name: "rate_locks", schema: "app");

        migrationBuilder.DropColumn(name: "expected_crypto_amount", schema: "app", table: "payments");
        migrationBuilder.DropColumn(name: "payment_address", schema: "app", table: "payments");
        migrationBuilder.DropColumn(name: "selected_currency", schema: "app", table: "payments");
    }
}
