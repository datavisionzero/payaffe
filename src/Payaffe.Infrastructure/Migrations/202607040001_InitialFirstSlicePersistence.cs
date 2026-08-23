using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607040001_InitialFirstSlicePersistence")]
public partial class InitialFirstSlicePersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "app");
        migrationBuilder.EnsureSchema(name: "auth");

        migrationBuilder.CreateTable(
            name: "integration_api_credentials",
            schema: "auth",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                token_hash = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_integration_api_credentials", x => x.id);
                table.CheckConstraint("ck_integration_api_credentials_status", "status in ('active', 'disabled')");
            });

        migrationBuilder.CreateTable(
            name: "payments",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                integration_api_credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                external_reference = table.Column<string>(type: "text", nullable: false),
                fiat_currency = table.Column<string>(type: "text", nullable: false),
                fiat_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                payer_page_id = table.Column<string>(type: "text", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                late_acceptance_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                context_username = table.Column<string>(type: "text", nullable: true),
                context_customer_number = table.Column<string>(type: "text", nullable: true),
                context_cart_name = table.Column<string>(type: "text", nullable: true),
                context_note = table.Column<string>(type: "text", nullable: true),
                return_url = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payments", x => x.id);
                table.CheckConstraint("ck_payments_status", "status in ('pending_currency_selection', 'waiting_for_payment', 'observed', 'completed', 'expired', 'settled')");
                table.ForeignKey(
                    name: "fk_payments_integration_api_credentials",
                    column: x => x.integration_api_credential_id,
                    principalSchema: "auth",
                    principalTable: "integration_api_credentials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "payment_creation_idempotency",
            schema: "app",
            columns: table => new
            {
                integration_api_credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key = table.Column<string>(type: "text", nullable: false),
                request_hash = table.Column<string>(type: "text", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payment_creation_idempotency", x => new { x.integration_api_credential_id, x.idempotency_key });
                table.ForeignKey(
                    name: "fk_payment_creation_idempotency_integration_api_credentials",
                    column: x => x.integration_api_credential_id,
                    principalSchema: "auth",
                    principalTable: "integration_api_credentials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_payment_creation_idempotency_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "payment_event_history",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                details = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payment_event_history", x => x.id);
                table.ForeignKey(
                    name: "fk_payment_event_history_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "payment_options",
            schema: "app",
            columns: table => new
            {
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                supported_currency = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                unavailable_reason = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payment_options", x => new { x.payment_id, x.supported_currency });
                table.CheckConstraint("ck_payment_options_status", "status in ('available', 'unavailable')");
                table.ForeignKey(
                    name: "fk_payment_options_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "uq_integration_api_credentials_token_hash",
            schema: "auth",
            table: "integration_api_credentials",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_payment_creation_idempotency_payment_id",
            schema: "app",
            table: "payment_creation_idempotency",
            column: "payment_id");

        migrationBuilder.CreateIndex(
            name: "uq_payment_creation_idempotency_credential_key",
            schema: "app",
            table: "payment_creation_idempotency",
            columns: ["integration_api_credential_id", "idempotency_key"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_payment_event_history_payment_id",
            schema: "app",
            table: "payment_event_history",
            column: "payment_id");

        migrationBuilder.CreateIndex(
            name: "ix_payments_integration_api_credential_id",
            schema: "app",
            table: "payments",
            column: "integration_api_credential_id");

        migrationBuilder.CreateIndex(
            name: "uq_payments_payer_page_id",
            schema: "app",
            table: "payments",
            column: "payer_page_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "payment_creation_idempotency", schema: "app");
        migrationBuilder.DropTable(name: "payment_event_history", schema: "app");
        migrationBuilder.DropTable(name: "payment_options", schema: "app");
        migrationBuilder.DropTable(name: "payments", schema: "app");
        migrationBuilder.DropTable(name: "integration_api_credentials", schema: "auth");
    }
}
