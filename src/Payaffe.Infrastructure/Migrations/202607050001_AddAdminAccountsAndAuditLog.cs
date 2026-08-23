using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607050001_AddAdminAccountsAndAuditLog")]
public partial class AddAdminAccountsAndAuditLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "audit");
        migrationBuilder.EnsureSchema(name: "auth");

        migrationBuilder.CreateTable(
            name: "admin_accounts",
            schema: "auth",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                username = table.Column<string>(type: "text", nullable: false),
                normalized_username = table.Column<string>(type: "text", nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: false),
                totp_secret_reference = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                failed_password_attempt_count = table.Column<int>(type: "integer", nullable: false),
                locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_password_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_admin_accounts", x => x.id);
                table.CheckConstraint("ck_admin_accounts_failed_password_attempt_count", "failed_password_attempt_count >= 0");
                table.CheckConstraint("ck_admin_accounts_status", "status in ('active', 'disabled')");
            });

        migrationBuilder.CreateTable(
            name: "audit_log_entries",
            schema: "audit",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                outcome = table.Column<string>(type: "text", nullable: false),
                actor_type = table.Column<string>(type: "text", nullable: false),
                actor_id = table.Column<string>(type: "text", nullable: false),
                source_service = table.Column<string>(type: "text", nullable: false),
                source_ip = table.Column<string>(type: "text", nullable: true),
                user_agent = table.Column<string>(type: "text", nullable: true),
                correlation_id = table.Column<string>(type: "text", nullable: false),
                reason_code = table.Column<string>(type: "text", nullable: false),
                subject_type = table.Column<string>(type: "text", nullable: false),
                subject_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit_log_entries", x => x.event_id);
                table.CheckConstraint("ck_audit_log_entries_actor_type", "actor_type in ('product_user', 'system')");
                table.CheckConstraint("ck_audit_log_entries_outcome", "outcome in ('success', 'failure', 'denied', 'expired', 'revoked')");
            });

        migrationBuilder.CreateIndex(
            name: "uq_admin_accounts_normalized_username",
            schema: "auth",
            table: "admin_accounts",
            column: "normalized_username",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_entries_event_type_occurred_at",
            schema: "audit",
            table: "audit_log_entries",
            columns: ["event_type", "occurred_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_audit_log_entries_occurred_at",
            schema: "audit",
            table: "audit_log_entries",
            column: "occurred_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "audit_log_entries", schema: "audit");
        migrationBuilder.DropTable(name: "admin_accounts", schema: "auth");
    }
}
