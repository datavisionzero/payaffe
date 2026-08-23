using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607050002_AddAdminLoginChallengesAndSessions")]
public partial class AddAdminLoginChallengesAndSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "admin_login_challenges",
            schema: "auth",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                failed_attempt_count = table.Column<int>(type: "integer", nullable: false),
                consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_admin_login_challenges", x => x.id);
                table.CheckConstraint("ck_admin_login_challenges_failed_attempt_count", "failed_attempt_count >= 0");
                table.ForeignKey(
                    name: "fk_admin_login_challenges_admin_accounts",
                    column: x => x.admin_account_id,
                    principalSchema: "auth",
                    principalTable: "admin_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "admin_sessions",
            schema: "auth",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                idle_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                mfa_authenticated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_admin_sessions", x => x.id);
                table.ForeignKey(
                    name: "fk_admin_sessions_admin_accounts",
                    column: x => x.admin_account_id,
                    principalSchema: "auth",
                    principalTable: "admin_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_admin_login_challenges_account_expires_at",
            schema: "auth",
            table: "admin_login_challenges",
            columns: ["admin_account_id", "expires_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_admin_sessions_account_expires_at",
            schema: "auth",
            table: "admin_sessions",
            columns: ["admin_account_id", "expires_at"]);

        migrationBuilder.CreateIndex(
            name: "uq_admin_sessions_token_hash",
            schema: "auth",
            table: "admin_sessions",
            column: "token_hash",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "admin_login_challenges", schema: "auth");
        migrationBuilder.DropTable(name: "admin_sessions", schema: "auth");
    }
}
