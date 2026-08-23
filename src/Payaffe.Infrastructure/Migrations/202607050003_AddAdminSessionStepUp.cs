using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607050003_AddAdminSessionStepUp")]
public partial class AddAdminSessionStepUp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "step_up_authenticated_at",
            schema: "auth",
            table: "admin_sessions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            """
            update auth.admin_sessions
            set step_up_authenticated_at = mfa_authenticated_at
            where step_up_authenticated_at is null
            """);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "step_up_authenticated_at",
            schema: "auth",
            table: "admin_sessions",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "step_up_authenticated_at",
            schema: "auth",
            table: "admin_sessions");
    }
}
