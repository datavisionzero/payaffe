using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(Persistence.PayaffeDbContext))]
[Migration("202607250002_AddPaymentAddressSources")]
public sealed class AddPaymentAddressSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "watch_only_wallet_cursors",
            schema: "app",
            columns: table => new
            {
                supported_currency = table.Column<string>(type: "text", nullable: false),
                source_fingerprint = table.Column<string>(type: "text", nullable: false),
                next_derivation_index = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_watch_only_wallet_cursors", row => row.supported_currency);
                table.CheckConstraint(
                    "ck_watch_only_wallet_cursors_currency",
                    "supported_currency in ('BTC', 'LTC')");
                table.CheckConstraint(
                    "ck_watch_only_wallet_cursors_next_index",
                    "next_derivation_index >= 0");
            });

        migrationBuilder.CreateTable(
            name: "native_eth_address_pool_imports",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                imported_by_admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                address_count = table.Column<int>(type: "integer", nullable: false),
                imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_native_eth_address_pool_imports", row => row.id);
                table.ForeignKey(
                    name: "fk_native_eth_address_pool_imports_admin_accounts",
                    column: row => row.imported_by_admin_account_id,
                    principalSchema: "auth",
                    principalTable: "admin_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "native_eth_addresses",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_id = table.Column<Guid>(type: "uuid", nullable: false),
                address = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                assigned_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_native_eth_addresses", row => row.id);
                table.CheckConstraint(
                    "ck_native_eth_addresses_status",
                    "status in ('unused', 'assigned', 'retired')");
                table.ForeignKey(
                    name: "fk_native_eth_addresses_imports",
                    column: row => row.import_id,
                    principalSchema: "app",
                    principalTable: "native_eth_address_pool_imports",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_native_eth_addresses_assigned_payments",
                    column: row => row.assigned_payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_native_eth_address_pool_imports_admin_account_id",
            schema: "app",
            table: "native_eth_address_pool_imports",
            column: "imported_by_admin_account_id");
        migrationBuilder.CreateIndex(
            name: "ix_native_eth_addresses_import_id",
            schema: "app",
            table: "native_eth_addresses",
            column: "import_id");
        migrationBuilder.CreateIndex(
            name: "ix_native_eth_addresses_status_created_at",
            schema: "app",
            table: "native_eth_addresses",
            columns: ["status", "created_at"]);
        migrationBuilder.CreateIndex(
            name: "uq_native_eth_addresses_address",
            schema: "app",
            table: "native_eth_addresses",
            column: "address",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "uq_native_eth_addresses_assigned_payment_id",
            schema: "app",
            table: "native_eth_addresses",
            column: "assigned_payment_id",
            unique: true,
            filter: "assigned_payment_id is not null");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "native_eth_addresses", schema: "app");
        migrationBuilder.DropTable(name: "native_eth_address_pool_imports", schema: "app");
        migrationBuilder.DropTable(name: "watch_only_wallet_cursors", schema: "app");
    }
}
