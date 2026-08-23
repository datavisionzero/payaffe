using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607040006_AddPaymentCompletionEvidence")]
public partial class AddPaymentCompletionEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "confirmed_eligible_total",
            schema: "app",
            table: "payments",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "completed_at",
            schema: "app",
            table: "payments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "contributed_to_completion",
            schema: "app",
            table: "matching_blockchain_transactions",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "confirmed_eligible_total",
            schema: "app",
            table: "payments");

        migrationBuilder.DropColumn(
            name: "completed_at",
            schema: "app",
            table: "payments");

        migrationBuilder.DropColumn(
            name: "contributed_to_completion",
            schema: "app",
            table: "matching_blockchain_transactions");
    }
}
