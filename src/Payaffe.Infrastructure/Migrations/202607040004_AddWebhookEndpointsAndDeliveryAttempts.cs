using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607040004_AddWebhookEndpointsAndDeliveryAttempts")]
public partial class AddWebhookEndpointsAndDeliveryAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "webhook_endpoints",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                integration_api_credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                url = table.Column<string>(type: "text", nullable: false),
                secret_reference = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                event_types = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_webhook_endpoints", x => x.id);
                table.CheckConstraint("ck_webhook_endpoints_status", "status in ('active', 'disabled')");
                table.ForeignKey(
                    name: "fk_webhook_endpoints_integration_api_credentials",
                    column: x => x.integration_api_credential_id,
                    principalSchema: "auth",
                    principalTable: "integration_api_credentials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "webhook_delivery_attempts",
            schema: "outbox",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                webhook_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                webhook_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                attempt_number = table.Column<int>(type: "integer", nullable: false),
                attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                result = table.Column<string>(type: "text", nullable: false),
                http_status_code = table.Column<int>(type: "integer", nullable: true),
                safe_error_code = table.Column<string>(type: "text", nullable: true),
                next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                correlation_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_webhook_delivery_attempts", x => x.id);
                table.CheckConstraint("ck_webhook_delivery_attempts_result", "result in ('succeeded', 'retry_pending', 'terminal_failed')");
                table.ForeignKey(
                    name: "fk_webhook_delivery_attempts_webhook_endpoints",
                    column: x => x.webhook_endpoint_id,
                    principalSchema: "app",
                    principalTable: "webhook_endpoints",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_webhook_delivery_attempts_webhook_events",
                    column: x => x.webhook_event_id,
                    principalSchema: "outbox",
                    principalTable: "webhook_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_webhook_delivery_attempts_webhook_endpoint_id",
            schema: "outbox",
            table: "webhook_delivery_attempts",
            column: "webhook_endpoint_id");

        migrationBuilder.CreateIndex(
            name: "ix_webhook_delivery_attempts_webhook_event_id",
            schema: "outbox",
            table: "webhook_delivery_attempts",
            column: "webhook_event_id");

        migrationBuilder.CreateIndex(
            name: "ix_webhook_endpoints_integration_api_credential_id",
            schema: "app",
            table: "webhook_endpoints",
            column: "integration_api_credential_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "webhook_delivery_attempts", schema: "outbox");
        migrationBuilder.DropTable(name: "webhook_endpoints", schema: "app");
    }
}
