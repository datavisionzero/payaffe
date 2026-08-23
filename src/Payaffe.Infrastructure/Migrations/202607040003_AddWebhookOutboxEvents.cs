using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607040003_AddWebhookOutboxEvents")]
public partial class AddWebhookOutboxEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "outbox");

        migrationBuilder.CreateTable(
            name: "webhook_events",
            schema: "outbox",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                integration_api_credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                event_version = table.Column<string>(type: "text", nullable: false),
                payload_version = table.Column<int>(type: "integer", nullable: false),
                resource_type = table.Column<string>(type: "text", nullable: false),
                resource_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                locked_by = table.Column<string>(type: "text", nullable: true),
                locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error_code = table.Column<string>(type: "text", nullable: true),
                correlation_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_webhook_events", x => x.id);
                table.CheckConstraint("ck_webhook_events_status", "status in ('pending', 'claimed', 'retry_pending', 'delivered', 'terminal_failed', 'cancelled')");
                table.ForeignKey(
                    name: "fk_webhook_events_integration_api_credentials",
                    column: x => x.integration_api_credential_id,
                    principalSchema: "auth",
                    principalTable: "integration_api_credentials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_webhook_events_payments",
                    column: x => x.payment_id,
                    principalSchema: "app",
                    principalTable: "payments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_webhook_events_integration_api_credential_id",
            schema: "outbox",
            table: "webhook_events",
            column: "integration_api_credential_id");

        migrationBuilder.CreateIndex(
            name: "ix_webhook_events_payment_id",
            schema: "outbox",
            table: "webhook_events",
            column: "payment_id");

        migrationBuilder.CreateIndex(
            name: "ix_webhook_events_status_next_attempt_at",
            schema: "outbox",
            table: "webhook_events",
            columns: ["status", "next_attempt_at"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "webhook_events", schema: "outbox");
    }
}
