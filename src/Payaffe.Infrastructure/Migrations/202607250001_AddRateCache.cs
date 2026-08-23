using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607250001_AddRateCache")]
public partial class AddRateCache : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rate_cache",
            schema: "app",
            columns: table => new
            {
                fiat_currency = table.Column<string>(type: "text", nullable: false),
                supported_currency = table.Column<string>(type: "text", nullable: false),
                rate_source = table.Column<string>(type: "text", nullable: false),
                rate_value = table.Column<string>(type: "text", nullable: false),
                observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_rate_cache",
                    x => new { x.fiat_currency, x.supported_currency });
                table.CheckConstraint(
                    "ck_rate_cache_fiat_currency",
                    "fiat_currency in ('EUR', 'USD')");
                table.CheckConstraint(
                    "ck_rate_cache_supported_currency",
                    "supported_currency in ('BTC', 'LTC', 'ETH')");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "rate_cache", schema: "app");
    }
}
