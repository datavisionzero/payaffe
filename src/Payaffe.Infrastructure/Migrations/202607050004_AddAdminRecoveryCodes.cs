using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607050004_AddAdminRecoveryCodes")]
public partial class AddAdminRecoveryCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "admin_recovery_codes",
            schema: "auth",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                admin_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                code_hash = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_admin_recovery_codes", x => x.id);
                table.CheckConstraint("ck_admin_recovery_codes_status", "status in ('active', 'used', 'revoked')");
                table.ForeignKey(
                    name: "fk_admin_recovery_codes_admin_accounts",
                    column: x => x.admin_account_id,
                    principalSchema: "auth",
                    principalTable: "admin_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_admin_recovery_codes_account_status",
            schema: "auth",
            table: "admin_recovery_codes",
            columns: ["admin_account_id", "status"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "admin_recovery_codes", schema: "auth");
    }
}
