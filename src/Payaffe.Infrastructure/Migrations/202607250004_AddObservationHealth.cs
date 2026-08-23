using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(Persistence.PayaffeDbContext))]
[Migration("202607250004_AddObservationHealth")]
public sealed class AddObservationHealth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "observation_health",
            schema: "app",
            columns: table => new
            {
                supported_currency = table.Column<string>(type: "text", nullable: false),
                provider_name = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                last_successful_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_safe_error_code = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_observation_health", row => row.supported_currency);
                table.CheckConstraint(
                    "ck_observation_health_currency",
                    "supported_currency in ('BTC', 'LTC', 'ETH')");
                table.CheckConstraint(
                    "ck_observation_health_status",
                    "status in ('available', 'unavailable')");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "observation_health", schema: "app");
    }
}
