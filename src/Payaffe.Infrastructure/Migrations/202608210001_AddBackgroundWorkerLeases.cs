using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202608210001_AddBackgroundWorkerLeases")]
public partial class AddBackgroundWorkerLeases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "background_worker_leases",
            schema: "app",
            columns: table => new
            {
                worker_name = table.Column<string>(type: "text", nullable: false),
                locked_by = table.Column<string>(type: "text", nullable: true),
                locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_safe_error_code = table.Column<string>(type: "text", nullable: true),
                consecutive_failure_count = table.Column<int>(type: "integer", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_background_worker_leases", row => row.worker_name);
                table.CheckConstraint(
                    "ck_background_worker_leases_failure_count",
                    "consecutive_failure_count >= 0");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "background_worker_leases", schema: "app");
}
